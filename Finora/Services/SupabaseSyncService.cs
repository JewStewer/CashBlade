using Finora.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Finora.Services;

public static class SupabaseSyncService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private static SupabaseConfig? _config;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static void Start()
    {
        _config = LoadConfig();
        if (_config is null)
        {
            Log("No supabase.json found — cloud sync disabled.");
            return;
        }

        Log($"Cloud sync enabled. Pushing to {_config.Url} every 5 minutes.");

        _ = Task.Run(async () =>
        {
            // Push immediately on startup, then every 5 minutes
            await PushAsync();
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                await PushAsync();
            }
        });
    }

    public static async Task PushAsync()
    {
        if (_config is null) return;
        try
        {
            Log("Cloud sync push started.");
            using var db = new FinoraDbContext();
            // AsNoTracking prevents EF Core from wiring up navigation properties
            // which would cause circular reference errors when serialising to JSON
            var payload = new
            {
                id = "main",
                syncedAt = DateTime.UtcNow,
                accounts = await db.Accounts.AsNoTracking().ToListAsync(),
                categories = await db.Categories.AsNoTracking().ToListAsync(),
                transactions = await db.Transactions.AsNoTracking().ToListAsync(),
                bills = await db.Bills.AsNoTracking().ToListAsync(),
                billOccurrenceStatuses = await db.BillOccurrenceStatuses.AsNoTracking().ToListAsync(),
                debts = await db.Debts.AsNoTracking().ToListAsync(),
                debtPayments = await db.DebtPayments.AsNoTracking().ToListAsync(),
                savingsGoals = await db.SavingsGoals.AsNoTracking().ToListAsync(),
                weeklyBudgets = await db.WeeklyBudgets.AsNoTracking().ToListAsync(),
                appSettings = await db.AppSettings.AsNoTracking().ToListAsync()
            };

            var baseUrl = NormaliseUrl(_config.Url);
            var json = JsonSerializer.Serialize(new { id = "main", payload = JsonSerializer.Serialize(payload, _opts), synced_at = DateTime.UtcNow }, _opts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/v1/finance_sync")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddSupabaseHeaders(req, _config.AnonKey);
            req.Headers.Add("Prefer", "resolution=merge-duplicates");

            var resp = await _http.SendAsync(req);
            Log(resp.IsSuccessStatusCode ? "Cloud sync push succeeded." : $"Cloud sync push failed: {resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Log($"Cloud sync push error: {ex.Message}");
        }
    }

    private static SupabaseConfig? LoadConfig()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "supabase.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cashglade", "supabase.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<SupabaseConfig>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!string.IsNullOrWhiteSpace(cfg?.Url) && !string.IsNullOrWhiteSpace(cfg?.AnonKey))
                    return cfg;
            }
            catch { }
        }
        return null;
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cashglade");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"),
                $"[{DateTimeOffset.Now:O}] SupabaseSync: {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void AddSupabaseHeaders(HttpRequestMessage req, string key)
    {
        req.Headers.Add("apikey", key);
        req.Headers.Add("Authorization", $"Bearer {key}");
    }

    private static string NormaliseUrl(string url)
    {
        url = url.TrimEnd('/');
        if (url.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^8];
        return url;
    }

    private class SupabaseConfig
    {
        public string Url { get; set; } = string.Empty;
        public string AnonKey { get; set; } = string.Empty;
    }
}
