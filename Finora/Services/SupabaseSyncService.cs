using Finora.Api;
using Finora.Data;
using Finora.Models;
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

            // Pull and apply any phone-side changes first so they're included in this push
            await ApplyPhonePushAsync(db);
            await db.SaveChangesAsync();
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

    // ── Read phone-pushed changes from Supabase and apply to local DB ────────────
    private static async Task ApplyPhonePushAsync(FinoraDbContext db)
    {
        if (_config is null) return;
        try
        {
            var baseUrl = NormaliseUrl(_config.Url);
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/phone_push?id=eq.main&select=payload");
            AddSupabaseHeaders(req, _config.AnonKey);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            var body = await resp.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<PhonePushRow>>(body, _opts);
            if (rows is null || rows.Count == 0 || string.IsNullOrWhiteSpace(rows[0].Payload)) return;

            var push = JsonSerializer.Deserialize<PushPayload>(rows[0].Payload, _opts);
            if (push is null) return;

            // New transactions (phone-created, negative temp IDs)
            foreach (var t in push.NewTransactions)
            {
                db.Transactions.Add(new Transaction
                {
                    Date = t.Date, Description = t.Description, AmountCents = t.AmountCents,
                    AccountId = t.AccountId, CategoryId = t.CategoryId,
                    TransferId = t.TransferId, IsUnnecessary = t.IsUnnecessary
                });
            }

            // Updated transactions
            foreach (var t in push.UpdatedTransactions.Where(x => x.Id > 0))
            {
                var existing = await db.Transactions.FindAsync(t.Id);
                if (existing is null) continue;
                existing.Description = t.Description;
                existing.AmountCents = t.AmountCents;
                existing.Date = t.Date;
                existing.AccountId = t.AccountId;
                existing.CategoryId = t.CategoryId;
                existing.IsUnnecessary = t.IsUnnecessary;
            }

            // Deleted transactions
            foreach (var id in push.DeletedTransactionIds)
            {
                var existing = await db.Transactions.FindAsync(id);
                if (existing is not null) db.Transactions.Remove(existing);
            }

            // Bill occurrence statuses (paid/unpaid overrides)
            foreach (var s in push.UpdatedBillStatuses)
            {
                var existing = await db.BillOccurrenceStatuses
                    .FirstOrDefaultAsync(x => x.BillId == s.BillId && x.DueDate.Date == s.DueDate.Date);
                if (existing is null)
                    db.BillOccurrenceStatuses.Add(new BillOccurrenceStatus
                    {
                        BillId = s.BillId, DueDate = s.DueDate,
                        IsPaid = s.IsPaid, IsSkipped = s.IsSkipped, PaidOn = s.PaidOn
                    });
                else
                {
                    existing.IsPaid = s.IsPaid;
                    existing.PaidOn = s.PaidOn;
                }

                // Mirror paid status on Bill itself
                var bill = await db.Bills.FindAsync(s.BillId);
                if (bill is not null) bill.IsPaid = s.IsPaid;
            }

            // New bills from phone
            foreach (var b in push.NewBills)
                db.Bills.Add(new Bill
                {
                    Name = b.Name, AccountId = b.AccountId, AmountCents = b.AmountCents,
                    DueDate = b.DueDate, Frequency = (BillFrequency)(int)b.Frequency, IsAutoPay = b.IsAutoPay
                });

            // Updated bills from phone
            foreach (var b in push.UpdatedBills.Where(x => x.Id > 0))
            {
                var existing = await db.Bills.FindAsync(b.Id);
                if (existing is null) continue;
                existing.Name = b.Name; existing.AccountId = b.AccountId;
                existing.AmountCents = b.AmountCents; existing.DueDate = b.DueDate;
                existing.Frequency = (BillFrequency)(int)b.Frequency; existing.IsAutoPay = b.IsAutoPay;
            }

            // Delete the phone_push row so it isn't applied again
            var del = new HttpRequestMessage(HttpMethod.Delete,
                $"{baseUrl}/rest/v1/phone_push?id=eq.main");
            AddSupabaseHeaders(del, _config.AnonKey);
            await _http.SendAsync(del);

            Log("Applied phone push from Supabase.");
        }
        catch (Exception ex) { Log($"ApplyPhonePush error: {ex.Message}"); }
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

    private class PhonePushRow
    {
        public string Payload { get; set; } = string.Empty;
    }
}
