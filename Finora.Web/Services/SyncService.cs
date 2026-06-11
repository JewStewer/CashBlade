using Finora.Web.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finora.Web.Services;

public class SyncService(HttpClient http, IndexedDbService db)
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public string? PcHost { get; private set; }
    public string? SupabaseUrl { get; private set; }
    public string? SupabaseKey { get; private set; }
    public DateTime? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }
    public bool IsSyncing { get; private set; }

    public bool HasCloudSync => !string.IsNullOrWhiteSpace(SupabaseUrl) && !string.IsNullOrWhiteSpace(SupabaseKey);
    public bool HasLocalSync => !string.IsNullOrWhiteSpace(PcHost);

    public event Action? OnSyncStateChanged;

    public async Task InitAsync()
    {
        var meta = await db.GetSyncMetaAsync();
        var first = meta.FirstOrDefault();
        if (first is not null)
        {
            PcHost = string.IsNullOrWhiteSpace(first.PcHost) ? null : first.PcHost;
            SupabaseUrl = string.IsNullOrWhiteSpace(first.SupabaseUrl) ? null : first.SupabaseUrl;
            SupabaseKey = string.IsNullOrWhiteSpace(first.SupabaseKey) ? null : first.SupabaseKey;
            LastSyncedAt = first.LastSyncedAt;
        }
    }

    public async Task SetPcHostAsync(string host)
    {
        host = host.TrimEnd('/');
        PcHost = host;
        await SaveMetaAsync();
        OnSyncStateChanged?.Invoke();
    }

    public async Task SetSupabaseAsync(string url, string key)
    {
        SupabaseUrl = NormaliseUrl(url);
        SupabaseKey = key.Trim();
        await SaveMetaAsync();
        OnSyncStateChanged?.Invoke();
    }

    private async Task SaveMetaAsync()
    {
        var meta = await db.GetSyncMetaAsync();
        var m = meta.FirstOrDefault() ?? new SyncMeta { Id = 1 };
        m.PcHost = PcHost ?? string.Empty;
        m.SupabaseUrl = SupabaseUrl ?? string.Empty;
        m.SupabaseKey = SupabaseKey ?? string.Empty;
        m.LastSyncedAt = LastSyncedAt;
        await db.PutAsync("syncMeta", m);
    }

    // ── Pull from Supabase (cloud) ─────────────────────────────────────────────
    public async Task<bool> SyncFromSupabaseAsync()
    {
        if (!HasCloudSync) return false;
        IsSyncing = true;
        LastError = null;
        OnSyncStateChanged?.Invoke();
        try
        {
            var baseUrl = NormaliseUrl(SupabaseUrl!);
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/finance_sync?id=eq.main&select=payload,synced_at");
            AddSupabaseHeaders(req, SupabaseKey!);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"Supabase returned {(int)resp.StatusCode}. Check your URL and key.";
                return false;
            }

            var body = await resp.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<SupabaseRow>>(body, _opts);
            if (rows is null || rows.Count == 0)
            {
                LastError = "No data in Supabase yet. Open Finance Blade on your PC to push data.";
                return false;
            }

            var payload = JsonSerializer.Deserialize<SyncPayload>(rows[0].Payload, _opts);
            if (payload is null) { LastError = "Could not read sync data."; return false; }

            await ApplyLocalTransactionIntentsAsync(payload);
            await db.SaveSyncPayloadAsync(payload);
            LastSyncedAt = rows[0].SyncedAt ?? payload.SyncedAt;
            await SaveMetaAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
            return false;
        }
        finally
        {
            IsSyncing = false;
            OnSyncStateChanged?.Invoke();
        }
    }

    // ── Pull from PC over Wi-Fi ───────────────────────────────────────────────
    public async Task<bool> SyncFromPcAsync()
    {
        if (!HasLocalSync) return false;
        IsSyncing = true;
        LastError = null;
        OnSyncStateChanged?.Invoke();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var payload = await http.GetFromJsonAsync<SyncPayload>($"{PcHost}/api/sync", _opts, cts.Token);
            if (payload is null) { LastError = "Empty response from PC."; return false; }
            await ApplyLocalTransactionIntentsAsync(payload);
            await db.SaveSyncPayloadAsync(payload);
            LastSyncedAt = payload.SyncedAt;
            await SaveMetaAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
            return false;
        }
        finally
        {
            IsSyncing = false;
            OnSyncStateChanged?.Invoke();
        }
    }

    // ── Auto-sync: try cloud first, fall back to local Wi-Fi if cloud fails ──────
    public async Task<bool> AutoSyncAsync()
    {
        if (HasCloudSync)
        {
            var ok = await SyncFromSupabaseAsync();
            if (ok) return true;
            // Cloud failed — fall through to local Wi-Fi if available
        }
        if (HasLocalSync) return await SyncFromPcAsync();
        if (!HasCloudSync)
        {
            LastError = "No sync configured. Add your Supabase details or PC address in Settings.";
            OnSyncStateChanged?.Invoke();
        }
        return false;
    }

    private async Task ApplyLocalTransactionIntentsAsync(SyncPayload payload)
    {
        var overrides = await db.GetPendingTransactionOverridesAsync();
        foreach (var ov in overrides)
        {
            var updated = ov.Transaction;
            var transaction = FindPayloadTransaction(payload.Transactions, updated);
            if (transaction is null) continue;

            transaction.Date = new DateTime(updated.Date.Year, updated.Date.Month, updated.Date.Day);
            transaction.Description = updated.Description;
            transaction.AmountCents = updated.AmountCents;
            transaction.AccountId = updated.AccountId;
            transaction.CategoryId = ResolvePayloadCategoryId(payload.Categories, updated.CategoryName, updated.CategoryId, updated.AmountCents);
            transaction.TransferId = updated.TransferId;
            transaction.UpTransactionId = updated.UpTransactionId;
            transaction.IsUnnecessary = updated.IsUnnecessary;
        }

        var deletes = await db.GetPendingTransactionDeletesAsync();
        foreach (var deleted in deletes.Select(d => d.Deleted))
        {
            var transaction = FindPayloadTransaction(payload.Transactions, deleted);
            if (transaction is not null)
                payload.Transactions.Remove(transaction);
        }
    }

    private static Transaction? FindPayloadTransaction(List<Transaction> transactions, Transaction updated)
    {
        if (!string.IsNullOrWhiteSpace(updated.UpTransactionId))
        {
            var byUpId = transactions.FirstOrDefault(t =>
                string.Equals(t.UpTransactionId, updated.UpTransactionId, StringComparison.Ordinal));
            if (byUpId is not null) return byUpId;
        }

        return transactions.FirstOrDefault(t => t.Id == updated.Id) ??
            transactions.FirstOrDefault(t =>
                t.Date.Date == updated.Date.Date &&
                t.AmountCents == updated.AmountCents &&
                string.Equals(t.Description, updated.Description, StringComparison.OrdinalIgnoreCase));
    }

    private static Transaction? FindPayloadTransaction(List<Transaction> transactions, TransactionDelete deleted)
    {
        if (!string.IsNullOrWhiteSpace(deleted.UpTransactionId))
        {
            var byUpId = transactions.FirstOrDefault(t =>
                string.Equals(t.UpTransactionId, deleted.UpTransactionId, StringComparison.Ordinal));
            if (byUpId is not null) return byUpId;
        }

        return transactions.FirstOrDefault(t => t.Id == deleted.Id) ??
            transactions.FirstOrDefault(t =>
                t.Date.Date == deleted.Date.Date &&
                t.AmountCents == deleted.AmountCents &&
                string.Equals(t.Description, deleted.Description, StringComparison.OrdinalIgnoreCase));
    }

    private static int ResolvePayloadCategoryId(List<Category> categories, string categoryName, int categoryId, int amountCents)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var byName = categories.FirstOrDefault(c => string.Equals(c.Name, categoryName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName.Id;
        }

        if (categories.Any(c => c.Id == categoryId)) return categoryId;

        var fallbackName = amountCents > 0 ? "Income" : "Misc";
        var fallback = categories.FirstOrDefault(c => string.Equals(c.Name, fallbackName, StringComparison.OrdinalIgnoreCase));
        if (fallback is not null) return fallback.Id;

        var created = new Category
        {
            Id = Math.Min(categories.Select(c => c.Id).DefaultIfEmpty(0).Min() - 1, -1),
            Name = fallbackName,
            Type = amountCents > 0 ? CategoryType.Income : CategoryType.Expense
        };
        categories.Add(created);
        return created.Id;
    }

    // ── Push phone changes to Supabase (phone_push table) ─────────────────────
    public async Task<bool> PushToSupabaseAsync(PushPayload push)
    {
        if (!HasCloudSync) return false;
        try
        {
            var baseUrl = NormaliseUrl(SupabaseUrl!);
            var body = JsonSerializer.Serialize(new
            {
                id = "main",
                payload = JsonSerializer.Serialize(push, _opts),
                pushed_at = DateTime.UtcNow
            }, _opts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/v1/phone_push")
            {
                Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            AddSupabaseHeaders(req, SupabaseKey!);
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var resp = await http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Fetch the current finance_sync payload without touching IndexedDB ────
    // Used to merge phone-side pending changes directly into the cloud snapshot.
    public async Task<SyncPayload?> FetchCloudPayloadAsync()
    {
        if (!HasCloudSync) return null;
        try
        {
            var baseUrl = NormaliseUrl(SupabaseUrl!);
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/finance_sync?id=eq.main&select=payload");
            AddSupabaseHeaders(req, SupabaseKey!);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<SupabaseRow>>(body, _opts);
            if (rows is null || rows.Count == 0 || string.IsNullOrWhiteSpace(rows[0].Payload)) return null;

            return JsonSerializer.Deserialize<SyncPayload>(rows[0].Payload, _opts);
        }
        catch { return null; }
    }

    // ── Push a merged full snapshot directly to finance_sync (the canonical ──
    // cloud state). Lets the phone keep the cloud current even if the PC never
    // comes back online; PushToSupabaseAsync (phone_push) still lets WPF
    // reconcile phone-assigned temp IDs whenever it's next opened.
    public async Task<bool> PushFullSyncAsync(SyncPayload payload)
    {
        if (!HasCloudSync) return false;
        try
        {
            var baseUrl = NormaliseUrl(SupabaseUrl!);
            var json = JsonSerializer.Serialize(new
            {
                id = "main",
                payload = JsonSerializer.Serialize(payload, _opts),
                synced_at = DateTime.UtcNow
            }, _opts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/v1/finance_sync")
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            AddSupabaseHeaders(req, SupabaseKey!);
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return false;

            LastSyncedAt = payload.SyncedAt;
            await SaveMetaAsync();
            OnSyncStateChanged?.Invoke();
            return true;
        }
        catch { return false; }
    }

    // ── Save push subscription to Supabase ───────────────────────────────────
    public async Task<bool> SavePushSubscriptionAsync(string subscriptionJson)
    {
        if (!HasCloudSync) return false;
        try
        {
            var baseUrl = NormaliseUrl(SupabaseUrl!);
            // Use endpoint URL as stable id (last 48 chars, url-safe)
            string endpoint = string.Empty;
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(subscriptionJson);
                endpoint = parsed.GetProperty("endpoint").GetString() ?? string.Empty;
            }
            catch { }
            var id = endpoint.Length > 48
                ? endpoint[^48..].Replace("/", "_").Replace(":", "_")
                : endpoint.Replace("/", "_").Replace(":", "_");
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                id,
                subscription = subscriptionJson,
                created_at = DateTime.UtcNow
            }, _opts);
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"{baseUrl}/rest/v1/push_subscriptions")
            {
                Content = new System.Net.Http.StringContent(body,
                    System.Text.Encoding.UTF8, "application/json")
            };
            AddSupabaseHeaders(req, SupabaseKey!);
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await http.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Push phone changes to PC ───────────────────────────────────────────────
    public async Task<bool> PushToPcAsync(PushPayload push)
    {
        if (!HasLocalSync) return false;
        try
        {
            var resp = await http.PostAsJsonAsync($"{PcHost}/api/sync/push", push, _opts);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> TestPcConnectionAsync(string host)
    {
        try
        {
            host = host.TrimEnd('/');
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await http.GetAsync($"{host}/api/ping", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public string? LastTestDetail { get; private set; }

    public async Task<bool> TestSupabaseAsync(string url, string key)
    {
        LastTestDetail = null;
        try
        {
            var baseUrl = NormaliseUrl(url);
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/finance_sync?select=synced_at&limit=1");
            AddSupabaseHeaders(req, key);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await http.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync();
            LastTestDetail = $"HTTP {(int)resp.StatusCode} — {body[..Math.Min(body.Length, 200)]}";
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            LastTestDetail = $"Exception: {ex.Message}";
            return false;
        }
    }

    // Supabase PostgREST needs both headers regardless of key format
    private static void AddSupabaseHeaders(HttpRequestMessage req, string key)
    {
        req.Headers.Add("apikey", key);
        req.Headers.Add("Authorization", $"Bearer {key}");
    }

    // Normalise URL — add https:// if missing, strip /rest/v1 if copy-pasted
    private static string NormaliseUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (url.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^8];
        return url;
    }

    private class SupabaseRow
    {
        public string Payload { get; set; } = string.Empty;
        public DateTime? SyncedAt { get; set; }
    }
}
