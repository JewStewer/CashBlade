using Finora.Web.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finora.Web.Services;

public class SyncService(HttpClient http, IndexedDbService db)
{
    private const string CategoryManagementRulesSettingKey = "CategoryManagementRules";
    private const string TransactionCategoryRulesSettingKey = "TransactionCategoryRules";

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
    public string? LastPushError { get; private set; }
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
        var localSettings = await db.GetAppSettingsAsync();

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
            transaction.IsReimbursement = updated.IsReimbursement;
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

        var billOverrides = await db.GetPendingBillEditOverridesAsync();
        foreach (var ov in billOverrides)
        {
            var updated = ov.Bill;
            if (deletedBills.Any(d => SameBillDelete(updated, d.ToBillDelete()))) continue;
            var bill = payload.Bills.FirstOrDefault(b => b.Id == updated.Id)
                ?? payload.Bills.FirstOrDefault(b => SameBillDelete(b, ToBillDelete(updated)));
            if (bill is null) payload.Bills.Add(updated);
            else CopyBillFields(updated, bill);
        }

        var debtOverrides = await db.GetPendingDebtOverridesAsync();
        foreach (var ov in debtOverrides)
        {
            var updated = ov.Debt;
            if (deletedDebts.Any(d => SameDebtDelete(updated, d))) continue;
            var debt = payload.Debts.FirstOrDefault(d => d.Id == updated.Id)
                ?? payload.Debts.FirstOrDefault(d => SameDebtSnapshot(d, updated));
            if (debt is null) payload.Debts.Add(updated);
            else CopyDebtFields(updated, debt);
        }

        var savingsGoalOverrides = await db.GetPendingSavingsGoalOverridesAsync();
        foreach (var ov in savingsGoalOverrides)
        {
            var updated = ov.Goal;
            if (deletedSavingsGoals.Any(d => SameSavingsGoalDelete(updated, d))) continue;
            var goal = payload.SavingsGoals.FirstOrDefault(g => g.Id == updated.Id)
                ?? payload.SavingsGoals.FirstOrDefault(g => SameSavingsGoalSnapshot(g, updated));
            if (goal is null) payload.SavingsGoals.Add(updated);
            else CopySavingsGoalFields(updated, goal);
        }

        var accountOverrides = await db.GetPendingAccountOverridesAsync();
        foreach (var ov in accountOverrides)
        {
            var updated = ov.Account;
            var account = payload.Accounts.FirstOrDefault(a => a.Id == updated.Id);
            if (account is null) continue;
            account.TargetCents = updated.TargetCents;
            account.TargetDate = updated.TargetDate;
            account.TargetStartDate = updated.TargetStartDate;
            account.TargetStartingBalanceCents = updated.TargetStartingBalanceCents;
        }

        var tripOverrides = await db.GetPendingTripOverridesAsync();
        foreach (var ov in tripOverrides)
        {
            var updated = ov.Trip;
            var trip = payload.Trips.FirstOrDefault(t => t.Id == updated.Id)
                ?? payload.Trips.FirstOrDefault(t => SameTripAdoptionCandidate(t, updated));
            if (trip is null) continue;

            CopyTripFields(updated, trip);
        }

        ApplyManagedCategoryRules(payload, localSettings);
        ApplyTransactionCategoryRules(payload, localSettings);

        // CategoryLimit:* and other phone-only keys are invisible to WPF, so
        // every WPF push overwrites appSettings without them.  Re-add any local
        // key the incoming snapshot doesn't include so they survive the replaceAll
        // that db.SaveSyncPayloadAsync performs.
        foreach (var local in localSettings)
        {
            if (!payload.AppSettings.Any(s => s.Key == local.Key))
                payload.AppSettings.Add(local);
        }

        // A setting changed locally (e.g. payday) but not yet confirmed pushed
        // must win over the incoming snapshot even when both sides know the key
        // (NextPayDate, SummaryPeriod, etc.) — otherwise a stale snapshot silently
        // reverts it the moment this pull replaces appSettings wholesale.
        var settingOverrides = await db.GetPendingSettingOverridesAsync();
        foreach (var ov in settingOverrides)
        {
            var existing = payload.AppSettings.FirstOrDefault(s => s.Key == ov.Setting.Key);
            if (existing is not null) existing.Value = ov.Setting.Value;
            else payload.AppSettings.Add(ov.Setting);
        }
    }

    private sealed class CategoryManagementRules
    {
        public List<ManagedCategoryRule> AddedCategories { get; set; } = new();
        public List<DeletedCategoryRule> DeletedCategories { get; set; } = new();
    }

    private sealed class ManagedCategoryRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public CategoryType Type { get; set; } = CategoryType.Expense;
    }

    private sealed class DeletedCategoryRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public CategoryType Type { get; set; } = CategoryType.Expense;
        public int ReplacementId { get; set; }
        public string ReplacementName { get; set; } = string.Empty;
    }

    private sealed class TransactionCategoryRules
    {
        public List<TransactionCategoryRule> Rules { get; set; } = new();
    }

    private sealed class TransactionCategoryRule
    {
        public string NormalizedName { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
    }

    private void ApplyManagedCategoryRules(SyncPayload payload, IReadOnlyList<AppSetting> localSettings)
    {
        var raw = localSettings.FirstOrDefault(s => s.Key == CategoryManagementRulesSettingKey)?.Value
            ?? payload.AppSettings.FirstOrDefault(s => s.Key == CategoryManagementRulesSettingKey)?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return;

        CategoryManagementRules? rules;
        try { rules = JsonSerializer.Deserialize<CategoryManagementRules>(raw, _opts); }
        catch { return; }
        if (rules is null) return;

        foreach (var rule in rules.AddedCategories.Where(r => !string.IsNullOrWhiteSpace(r.Name)))
        {
            if (rules.DeletedCategories.Any(d =>
                d.Type == rule.Type &&
                string.Equals(d.Name, rule.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (payload.Categories.Any(c =>
                c.Type == rule.Type &&
                string.Equals(c.Name, rule.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var id = rule.Id < 0 && payload.Categories.All(c => c.Id != rule.Id)
                ? rule.Id
                : Math.Min(payload.Categories.Select(c => c.Id).DefaultIfEmpty(0).Min() - 1, -1);
            payload.Categories.Add(new Category { Id = id, Name = rule.Name.Trim(), Type = rule.Type });
        }

        foreach (var rule in rules.DeletedCategories.Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.ReplacementName)))
        {
            var replacement = payload.Categories.FirstOrDefault(c =>
                    c.Type == rule.Type &&
                    string.Equals(c.Name, rule.ReplacementName.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? payload.Categories.FirstOrDefault(c => c.Id == rule.ReplacementId && c.Type == rule.Type)
                ?? payload.Categories.FirstOrDefault(c =>
                    c.Type == rule.Type &&
                    !string.Equals(c.Name, rule.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (replacement is null) continue;

            var deletedIds = payload.Categories
                .Where(c => c.Type == rule.Type &&
                    (c.Id == rule.Id || string.Equals(c.Name, rule.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Id)
                .ToHashSet();

            foreach (var transaction in payload.Transactions.Where(t =>
                deletedIds.Contains(t.CategoryId) ||
                string.Equals(t.CategoryName, rule.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                transaction.CategoryId = replacement.Id;
                transaction.CategoryName = replacement.Name;
            }

            payload.Categories.RemoveAll(c => deletedIds.Contains(c.Id));
        }
    }

    private void ApplyTransactionCategoryRules(SyncPayload payload, IReadOnlyList<AppSetting> localSettings)
    {
        var raw = localSettings.FirstOrDefault(s => s.Key == TransactionCategoryRulesSettingKey)?.Value
            ?? payload.AppSettings.FirstOrDefault(s => s.Key == TransactionCategoryRulesSettingKey)?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return;

        TransactionCategoryRules? rules;
        try { rules = JsonSerializer.Deserialize<TransactionCategoryRules>(raw, _opts); }
        catch { return; }
        if (rules is null) return;

        foreach (var rule in rules.Rules.Where(r => !string.IsNullOrWhiteSpace(r.NormalizedName) && !string.IsNullOrWhiteSpace(r.CategoryName)))
        {
            var category = payload.Categories.FirstOrDefault(c =>
                    string.Equals(c.Name, rule.CategoryName.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? payload.Categories.FirstOrDefault(c => c.Id == rule.CategoryId);
            if (category is null) continue;

            foreach (var transaction in payload.Transactions.Where(t =>
                t.AmountCents < 0 &&
                string.Equals(NormalizeRecurringDescription(t.Description), rule.NormalizedName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                transaction.CategoryId = category.Id;
                transaction.CategoryName = category.Name;
            }
        }
    }

    private static string NormalizeRecurringDescription(string description)
    {
        var cleaned = description.Trim();
        var separatorIndex = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            cleaned = cleaned[..separatorIndex];
        }
        return cleaned;
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

    private static BillDelete ToBillDelete(Bill bill) => new()
    {
        Id = bill.Id,
        Name = bill.Name,
        AccountId = bill.AccountId,
        AccountName = bill.AccountName,
        AmountCents = bill.AmountCents,
        DueDate = bill.DueDate,
        Frequency = bill.Frequency
    };

    private static void CopyBillFields(Bill source, Bill target)
    {
        target.Name = source.Name;
        target.AccountId = source.AccountId;
        target.AccountName = source.AccountName;
        target.AmountDollars = source.AmountDollars;
        target.DueDate = source.DueDate;
        target.Frequency = source.Frequency;
        target.IsAutoPay = source.IsAutoPay;
        target.IsPaid = source.IsPaid;
        target.DebtId = source.DebtId;
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

    private static void CopyDebtFields(Debt source, Debt target)
    {
        target.Name = source.Name;
        target.BalanceCents = source.BalanceCents;
        target.MinimumPaymentCents = source.MinimumPaymentCents;
        target.PaymentPeriod = source.PaymentPeriod;
        target.InterestRate = source.InterestRate;
        target.OriginalBalanceCents = source.OriginalBalanceCents;
    }

    private static bool SameDebtSnapshot(Debt left, Debt right)
    {
        if (left.Id > 0 && right.Id > 0 && left.Id == right.Id) return true;
        if (!string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (left.OriginalBalanceCents > 0 && right.OriginalBalanceCents > 0)
            return left.OriginalBalanceCents == right.OriginalBalanceCents;
        return left.BalanceCents == right.BalanceCents;
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
        // Two distinct goals can share the same name and amounts but belong to
        // different groups (see the identical check in AppState.SameSavingsGoalDelete).
        if (!string.Equals((goal.GroupName ?? "").Trim(), (deleted.GroupName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (deleted.TargetCents > 0 && goal.TargetCents > 0)
            return goal.TargetCents == deleted.TargetCents;
        return goal.CurrentCents == deleted.CurrentCents;
    }

    private static void CopySavingsGoalFields(SavingsGoal source, SavingsGoal target)
    {
        target.Name = source.Name;
        target.TargetCents = source.TargetCents;
        target.CurrentCents = source.CurrentCents;
        target.WeeklyContributionCents = source.WeeklyContributionCents;
        target.TargetDate = source.TargetDate;
        target.TargetStartDate = source.TargetStartDate;
        target.TargetStartingBalanceCents = source.TargetStartingBalanceCents;
        target.GroupName = source.GroupName;
        target.Emoji = source.Emoji;
    }

    private static bool SameSavingsGoalSnapshot(SavingsGoal left, SavingsGoal right)
    {
        if (left.Id > 0 && right.Id > 0 && left.Id == right.Id) return true;
        if (!string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals((left.GroupName ?? "").Trim(), (right.GroupName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (left.TargetCents > 0 && right.TargetCents > 0)
            return left.TargetCents == right.TargetCents;
        return left.CurrentCents == right.CurrentCents;
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

    private static bool SameTripAdoptionCandidate(Trip serverTrip, Trip localTrip) =>
        serverTrip.Id > 0 &&
        localTrip.Id < 0 &&
        string.Equals(serverTrip.Name.Trim(), localTrip.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals((serverTrip.Destination ?? "").Trim(), (localTrip.Destination ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
        serverTrip.StartDate?.Date == localTrip.StartDate?.Date;

    private static void CopyTripFields(Trip source, Trip target)
    {
        target.Name = source.Name;
        target.Destination = source.Destination;
        target.Notes = source.Notes;
        target.StartDate = source.StartDate;
        target.EndDate = source.EndDate;
        target.SavingsAccountId = source.SavingsAccountId;
        target.WeeklyContributionCents = source.WeeklyContributionCents;
        target.Itinerary = source.Itinerary;
        target.Checklist = source.Checklist;
        target.BudgetItems = source.BudgetItems;
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
        LastPushError = null;
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
            if (!resp.IsSuccessStatusCode)
                LastPushError = $"phone_push HTTP {(int)resp.StatusCode}";
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            LastPushError = ex is TaskCanceledException ? "phone_push timed out" : $"phone_push: {ex.Message[..Math.Min(60, ex.Message.Length)]}";
            return false;
        }
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
            if (!resp.IsSuccessStatusCode)
            {
                LastPushError ??= $"cloud fetch HTTP {(int)resp.StatusCode}";
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<SupabaseRow>>(body, _opts);
            if (rows is null || rows.Count == 0 || string.IsNullOrWhiteSpace(rows[0].Payload))
            {
                LastPushError ??= "cloud fetch: empty response";
                return null;
            }

            var result = JsonSerializer.Deserialize<SyncPayload>(rows[0].Payload, _opts);
            if (result is null) LastPushError ??= "cloud fetch: payload deserialized as null";
            return result;
        }
        catch (Exception ex)
        {
            LastPushError ??= ex is TaskCanceledException ? "cloud fetch timed out" : $"cloud fetch: {ex.Message[..Math.Min(60, ex.Message.Length)]}";
            return null;
        }
    }

    // ── Push a merged full snapshot directly to finance_sync (the canonical ──
    // cloud state). Lets the phone keep the cloud current even if the PC never
    // comes back online; PushToSupabaseAsync (phone_push) still lets WPF
    // reconcile phone-assigned temp IDs whenever it's next opened.
    public async Task<bool> PushFullSyncAsync(SyncPayload payload)
    {
        if (!HasCloudSync) return false;
        if (IsEmptyFinancePayload(payload))
        {
            LastPushError = "Refused: merged snapshot is empty — sync from cloud first.";
            LastError = "Refusing to push an empty finance snapshot. Sync from Windows or Supabase first.";
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
            if (!resp.IsSuccessStatusCode)
            {
                LastPushError ??= $"finance_sync HTTP {(int)resp.StatusCode}";
                return false;
            }

            LastSyncedAt = payload.SyncedAt;
            await SaveMetaAsync();
            OnSyncStateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            LastPushError ??= ex is TaskCanceledException ? "finance_sync timed out" : $"finance_sync: {ex.Message[..Math.Min(60, ex.Message.Length)]}";
            return false;
        }
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
            // Unlike SyncFromPcAsync/TestPcConnectionAsync, this had no timeout —
            // with the PC off or unreachable, it could hang for the default
            // HttpClient timeout (100s) before falling through to Supabase,
            // leaving the "unsync'd" badge stuck the whole time.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await http.PostAsJsonAsync($"{PcHost}/api/sync/push", push, _opts, cts.Token);
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
