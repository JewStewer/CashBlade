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
                LastError = "No data in Supabase yet. Open Evergrove on your PC to push data.";
                return false;
            }

            var payload = JsonSerializer.Deserialize<SyncPayload>(rows[0].Payload, _opts);
            if (payload is null) { LastError = "Could not read sync data."; return false; }
            if (IsEmptyFinancePayload(payload))
            {
                LastError = "Supabase has an empty finance snapshot. Open Windows and sync/push the real data again.";
                return false;
            }
            if (IsMissingPlanningPayload(payload))
            {
                LastError = "Supabase is missing bills, debts, savings goals, and budget data. Open Windows and sync/push the real data again.";
                return false;
            }

            await ApplyLocalIntentsAsync(payload);
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
            if (IsEmptyFinancePayload(payload))
            {
                LastError = "PC returned an empty finance snapshot. Open Windows and confirm your data is visible there.";
                return false;
            }
            if (IsMissingPlanningPayload(payload))
            {
                LastError = "PC returned no bills, debts, savings goals, or budget data. Confirm Windows is using the right database.";
                return false;
            }

            await ApplyLocalIntentsAsync(payload);
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

    private async Task ApplyLocalIntentsAsync(SyncPayload payload)
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
        foreach (var pending in deletes)
        {
            var deleted = pending.Deleted;
            var transaction = FindPayloadTransaction(payload.Transactions, deleted);
            if (transaction is not null)
            {
                payload.Transactions.Remove(transaction);
            }
            else
            {
                // No longer present in the incoming snapshot, so the delete has
                // propagated server-side. Drop the tombstone so its signature
                // match can't keep suppressing unrelated future transactions.
                await db.ClearTransactionDeleteAsync(pending.Id);
            }
        }

        var deletedBills = await db.GetPendingBillDeletesAsync();
        foreach (var deleted in deletedBills.Select(d => d.ToBillDelete()))
        {
            var matchingIds = payload.Bills
                .Where(b => SameBillDelete(b, deleted))
                .Select(b => b.Id)
                .ToHashSet();
            var stillExistsInIncomingSnapshot = matchingIds.Count > 0;
            payload.Bills.RemoveAll(b => matchingIds.Contains(b.Id));
            payload.BillOccurrenceStatuses.RemoveAll(s => matchingIds.Contains(s.BillId));
            if (!stillExistsInIncomingSnapshot)
            {
                await db.ClearBillDeleteAsync(deleted.Id);
            }
        }

        var deletedDebts = await db.GetPendingDebtDeletesAsync();
        foreach (var deleted in deletedDebts)
        {
            var matchingIds = payload.Debts
                .Where(d => SameDebtDelete(d, deleted))
                .Select(d => d.Id)
                .ToHashSet();
            var stillExistsInIncomingSnapshot = matchingIds.Count > 0;
            payload.Debts.RemoveAll(d => matchingIds.Contains(d.Id));
            payload.DebtPayments.RemoveAll(p => matchingIds.Contains(p.DebtId));
            foreach (var bill in payload.Bills.Where(b => b.DebtId.HasValue && matchingIds.Contains(b.DebtId.Value)))
            {
                bill.DebtId = null;
            }
            if (!stillExistsInIncomingSnapshot)
            {
                await db.ClearDebtDeleteAsync(deleted.Id);
            }
        }

        var deletedSavingsGoals = await db.GetPendingSavingsGoalDeletesAsync();
        foreach (var deleted in deletedSavingsGoals)
        {
            var matchingIds = payload.SavingsGoals
                .Where(g => SameSavingsGoalDelete(g, deleted))
                .Select(g => g.Id)
                .ToHashSet();
            var stillExistsInIncomingSnapshot = matchingIds.Count > 0;
            payload.SavingsGoals.RemoveAll(g => matchingIds.Contains(g.Id));
            if (!stillExistsInIncomingSnapshot)
            {
                await db.ClearSavingsGoalDeleteAsync(deleted.Id);
            }
        }

        var deletedTrips = await db.GetPendingTripDeletesAsync();
        foreach (var deleted in deletedTrips)
        {
            var matchingIds = payload.Trips
                .Where(t => SameTripDelete(t, deleted))
                .Select(t => t.Id)
                .ToHashSet();
            var stillExistsInIncomingSnapshot = matchingIds.Count > 0;
            payload.Trips.RemoveAll(t => matchingIds.Contains(t.Id));
            if (!stillExistsInIncomingSnapshot)
            {
                await db.ClearTripDeleteAsync(deleted.Id);
            }
        }

        // CategoryLimit:* and other phone-only keys are invisible to WPF, so
        // every WPF push overwrites appSettings without them.  Re-add any local
        // key the incoming snapshot doesn't include so they survive the replaceAll
        // that db.SaveSyncPayloadAsync performs.
        var localSettings = await db.GetAppSettingsAsync();
        foreach (var local in localSettings)
        {
            if (!payload.AppSettings.Any(s => s.Key == local.Key))
                payload.AppSettings.Add(local);
        }
    }

    private static bool SameBillDelete(Bill bill, BillDelete deleted)
    {
        // IDs can be recycled (e.g. SQLite reuses a freed primary key), so an ID
        // match alone isn't proof it's the same bill. Require it to also share
        // its frequency plus its name or amount before treating it as a match.
        if (bill.Id > 0 && deleted.Id > 0 && bill.Id == deleted.Id
            && bill.Frequency == deleted.Frequency
            && (string.Equals(bill.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase) || bill.AmountCents == deleted.AmountCents))
            return true;
        if (bill.AmountCents != deleted.AmountCents) return false;
        if (bill.Frequency != deleted.Frequency) return false;
        if (!string.Equals(bill.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        var billAccount = string.IsNullOrWhiteSpace(bill.AccountName) ? bill.AccountId.ToString() : bill.AccountName.Trim();
        var deletedAccount = string.IsNullOrWhiteSpace(deleted.AccountName) ? deleted.AccountId.ToString() : deleted.AccountName.Trim();
        if (!string.Equals(billAccount, deletedAccount, StringComparison.OrdinalIgnoreCase) && bill.AccountId != deleted.AccountId) return false;
        return Math.Abs((bill.DueDate.Date - deleted.DueDate.Date).TotalDays) <= 7;
    }

    private static bool SameDebtDelete(Debt debt, PendingDebtDelete deleted)
    {
        if (debt.Id > 0 && deleted.Id > 0 && debt.Id == deleted.Id
            && (string.Equals(debt.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                || debt.BalanceCents == deleted.BalanceCents
                || debt.OriginalBalanceCents == deleted.OriginalBalanceCents))
        {
            return true;
        }

        if (!string.Equals(debt.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (deleted.OriginalBalanceCents > 0 && debt.OriginalBalanceCents > 0)
            return debt.OriginalBalanceCents == deleted.OriginalBalanceCents;
        return debt.BalanceCents == deleted.BalanceCents;
    }

    private static bool SameSavingsGoalDelete(SavingsGoal goal, PendingSavingsGoalDelete deleted)
    {
        if (goal.Id > 0 && deleted.Id > 0 && goal.Id == deleted.Id
            && (string.Equals(goal.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                || goal.TargetCents == deleted.TargetCents
                || goal.CurrentCents == deleted.CurrentCents))
        {
            return true;
        }

        if (!string.Equals(goal.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (deleted.TargetCents > 0 && goal.TargetCents > 0)
            return goal.TargetCents == deleted.TargetCents;
        return goal.CurrentCents == deleted.CurrentCents;
    }

    private static bool SameTripDelete(Trip trip, PendingTripDelete deleted)
    {
        if (trip.Id > 0 && deleted.Id > 0 && trip.Id == deleted.Id
            && (string.Equals(trip.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(trip.Destination?.Trim() ?? "", deleted.Destination?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
                || trip.StartDate == deleted.StartDate))
        {
            return true;
        }

        if (!string.Equals(trip.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (deleted.StartDate.HasValue && trip.StartDate.HasValue)
            return trip.StartDate == deleted.StartDate;
        return string.Equals(trip.Destination?.Trim() ?? "", deleted.Destination?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
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
            transactions.FirstOrDefault(t => SameTransactionSignature(t.Date, t.Description, t.AmountCents, updated.Date, updated.Description, updated.AmountCents));
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
            transactions.FirstOrDefault(t => SameTransactionSignature(t.Date, t.Description, t.AmountCents, deleted.Date, deleted.Description, deleted.AmountCents));
    }

    private static bool SameTransactionSignature(DateTime leftDate, string leftDescription, int leftAmountCents, DateTime rightDate, string rightDescription, int rightAmountCents)
    {
        if (leftAmountCents != rightAmountCents) return false;
        if (!string.Equals((leftDescription ?? string.Empty).Trim(), (rightDescription ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return Math.Abs((leftDate.Date - rightDate.Date).TotalDays) <= 3;
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
        if (IsEmptyFinancePayload(payload) || IsMissingPlanningPayload(payload))
        {
            LastError = IsEmptyFinancePayload(payload)
                ? "Refusing to push an empty finance snapshot. Sync from Windows or Supabase first."
                : "Refusing to push a snapshot with no bills, debts, savings goals, or budget data.";
            OnSyncStateChanged?.Invoke();
            return false;
        }

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
    private static bool IsEmptyFinancePayload(SyncPayload payload) =>
        payload.Accounts.Count == 0 &&
        payload.Transactions.Count == 0 &&
        payload.Bills.Count == 0 &&
        payload.Debts.Count == 0 &&
        payload.SavingsGoals.Count == 0 &&
        payload.WeeklyBudgets.Count == 0;

    private static bool IsMissingPlanningPayload(SyncPayload payload) =>
        payload.Bills.Count == 0 &&
        payload.Debts.Count == 0 &&
        payload.SavingsGoals.Count == 0 &&
        payload.WeeklyBudgets.Count == 0 &&
        payload.Trips.Count == 0 &&
        (payload.Accounts.Count > 0 || payload.Transactions.Count > 0);

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
