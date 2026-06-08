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
