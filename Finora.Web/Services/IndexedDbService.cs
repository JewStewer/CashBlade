using Finora.Web.Models;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Finora.Web.Services;

public class IndexedDbService(IJSRuntime js)
{
    private readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task<List<T>> GetAllAsync<T>(string store)
    {
        var raw = await js.InvokeAsync<JsonElement[]>("db.getAll", store);
        return raw.Select(e => JsonSerializer.Deserialize<T>(e.GetRawText(), _opts)!).ToList();
    }

    public async Task PutAsync<T>(string store, T record)
    {
        var json = JsonSerializer.Serialize(record, _opts);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        await js.InvokeVoidAsync("db.put", store, element);
    }

    public async Task PutBulkAsync<T>(string store, IEnumerable<T> records)
    {
        var elements = records
            .Select(r => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(r, _opts)))
            .ToArray();
        await js.InvokeVoidAsync("db.putBulk", store, elements);
    }

    public async Task DeleteAsync(string store, int id) =>
        await js.InvokeVoidAsync("db.delete", store, id);

    public async Task DeleteAsync(string store, string id) =>
        await js.InvokeVoidAsync("db.delete", store, id);

    public async Task ClearAsync(string store) =>
        await js.InvokeVoidAsync("db.clear", store);

    public async Task ClearAllAsync() =>
        await js.InvokeVoidAsync("db.clearAll");

    // Typed helpers
    public Task<List<Account>> GetAccountsAsync() => GetAllAsync<Account>("accounts");
    public Task<List<Category>> GetCategoriesAsync() => GetAllAsync<Category>("categories");
    public Task<List<Transaction>> GetTransactionsAsync() => GetAllAsync<Transaction>("transactions");
    public Task<List<Bill>> GetBillsAsync() => GetAllAsync<Bill>("bills");
    public Task<List<BillOccurrenceStatus>> GetBillStatusesAsync() => GetAllAsync<BillOccurrenceStatus>("billOccurrenceStatuses");
    public Task<List<Debt>> GetDebtsAsync() => GetAllAsync<Debt>("debts");
    public Task<List<DebtPayment>> GetDebtPaymentsAsync() => GetAllAsync<DebtPayment>("debtPayments");
    public Task<List<SavingsGoal>> GetSavingsGoalsAsync() => GetAllAsync<SavingsGoal>("savingsGoals");
    public Task<List<WeeklyBudget>> GetWeeklyBudgetsAsync() => GetAllAsync<WeeklyBudget>("weeklyBudgets");
    public Task<List<AppSetting>> GetAppSettingsAsync() => GetAllAsync<AppSetting>("appSettings");
    public Task<List<SyncMeta>> GetSyncMetaAsync() => GetAllAsync<SyncMeta>("syncMeta");
    public async Task<List<Trip>> GetTripsAsync()
    {
        try
        {
            return await GetAllAsync<Trip>("trips");
        }
        catch (JSException)
        {
            return new List<Trip>();
        }
    }

    public async Task SaveSyncPayloadAsync(SyncPayload p)
    {
        if (IsEmptyFinancePayload(p) && await HasLocalFinanceDataAsync())
        {
            return;
        }
        if (IsMissingPlanningPayload(p) && await HasLocalPlanningDataAsync())
        {
            return;
        }

        await PreserveLocalTripsWhenMissingAsync(p);

        // Preserve PC host / Supabase credentials stored in syncMeta
        var existingMeta = await GetSyncMetaAsync();
        var meta = existingMeta.FirstOrDefault() ?? new SyncMeta { Id = 1 };
        meta.LastSyncedAt = p.SyncedAt;

        // Bill overrides are intentionally outside replaceAll so unpaid/paid
        // changes survive a refresh until pushed. When the PC/cloud snapshot no
        // longer contains a bill, remove stale overrides so deleted series stay
        // deleted on iOS instead of lingering locally.
        var validBillIds = p.Bills.Select(b => b.Id).ToHashSet();
        var staleBillOverrides = await GetPendingBillOverridesAsync();
        foreach (var stale in staleBillOverrides.Where(o => !validBillIds.Contains(o.Id)))
        {
            await ClearBillOverrideAsync(stale.Id);
        }

        // Single atomic IndexedDB transaction via db.replaceAll:
        //   • clears all sync-managed stores
        //   • writes new records for every store
        // — all in one transaction so if anything fails, IndexedDB rolls back
        //   automatically and the existing data is preserved (not left half-wiped).
        var replacePayload = new
        {
            accounts               = p.Accounts,
            categories             = p.Categories,
            transactions           = p.Transactions,
            bills                  = p.Bills,
            billOccurrenceStatuses = p.BillOccurrenceStatuses,
            debts                  = p.Debts,
            debtPayments           = p.DebtPayments,
            savingsGoals           = p.SavingsGoals,
            weeklyBudgets          = p.WeeklyBudgets,
            appSettings            = p.AppSettings,
            trips                  = p.Trips,
            syncMeta               = meta
        };

        var json    = JsonSerializer.Serialize(replacePayload, _opts);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        await js.InvokeVoidAsync("db.replaceAll", element);
    }

    private async Task PreserveLocalTripsWhenMissingAsync(SyncPayload p)
    {
        var localTrips = await GetTripsAsync();
        if (localTrips.Count == 0) return;

        if (p.Trips.Count == 0)
        {
            p.Trips = localTrips;
            return;
        }

        foreach (var local in localTrips)
        {
            var incoming = p.Trips.FirstOrDefault(t => t.Id == local.Id)
                ?? p.Trips.FirstOrDefault(t =>
                    string.Equals(t.Name.Trim(), local.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    t.StartDate?.Date == local.StartDate?.Date);
            if (incoming is null)
            {
                p.Trips.Add(local);
                continue;
            }

            if (IsTripRicher(local, incoming))
            {
                var index = p.Trips.IndexOf(incoming);
                p.Trips[index] = local;
            }
        }
    }

    private static bool IsTripRicher(Trip local, Trip incoming)
    {
        var localScore =
            local.Itinerary.Count +
            local.Checklist.Count +
            local.BudgetItems.Count +
            (string.IsNullOrWhiteSpace(local.Notes) ? 0 : 1) +
            (local.SavingsAccountId is null ? 0 : 1) +
            (local.WeeklyContributionCents > 0 ? 1 : 0);
        var incomingScore =
            incoming.Itinerary.Count +
            incoming.Checklist.Count +
            incoming.BudgetItems.Count +
            (string.IsNullOrWhiteSpace(incoming.Notes) ? 0 : 1) +
            (incoming.SavingsAccountId is null ? 0 : 1) +
            (incoming.WeeklyContributionCents > 0 ? 1 : 0);
        return localScore > incomingScore;
    }

    private async Task<bool> HasLocalFinanceDataAsync() =>
        (await GetAccountsAsync()).Count > 0 ||
        (await GetTransactionsAsync()).Count > 0 ||
        (await GetBillsAsync()).Count > 0 ||
        (await GetDebtsAsync()).Count > 0 ||
        (await GetSavingsGoalsAsync()).Count > 0 ||
        (await GetWeeklyBudgetsAsync()).Count > 0;

    private async Task<bool> HasLocalPlanningDataAsync() =>
        (await GetBillsAsync()).Count > 0 ||
        (await GetDebtsAsync()).Count > 0 ||
        (await GetSavingsGoalsAsync()).Count > 0 ||
        (await GetWeeklyBudgetsAsync()).Count > 0 ||
        (await GetTripsAsync()).Count > 0;

    private static bool IsEmptyFinancePayload(SyncPayload p) =>
        p.Accounts.Count == 0 &&
        p.Transactions.Count == 0 &&
        p.Bills.Count == 0 &&
        p.Debts.Count == 0 &&
        p.SavingsGoals.Count == 0 &&
        p.WeeklyBudgets.Count == 0;

    private static bool IsMissingPlanningPayload(SyncPayload p) =>
        p.Bills.Count == 0 &&
        p.Debts.Count == 0 &&
        p.SavingsGoals.Count == 0 &&
        p.WeeklyBudgets.Count == 0 &&
        p.Trips.Count == 0 &&
        (p.Accounts.Count > 0 || p.Transactions.Count > 0);

    public async Task<string?> GetSettingAsync(string key)
    {
        var settings = await GetAppSettingsAsync();
        return settings.FirstOrDefault(s => s.Key == key)?.Value;
    }

    // ── Lent transactions (survive clearAll; phone-only tracking) ──────────────
    public Task<List<LentTransaction>> GetLentTransactionsAsync() =>
        GetAllAsync<LentTransaction>("lentTxns");

    public Task SetLentTransactionAsync(LentTransaction lent) =>
        PutAsync("lentTxns", lent);

    public Task DeleteLentTransactionAsync(int id) =>
        DeleteAsync("lentTxns", id);

    // ── Prepaid cards (phone-only label over a real Account; no separate ledger) ──
    public Task<List<PrepaidCard>> GetPrepaidCardsAsync() =>
        GetAllAsync<PrepaidCard>("prepaidCards");

    public Task SetPrepaidCardAsync(PrepaidCard card) =>
        PutAsync("prepaidCards", card);

    public Task DeletePrepaidCardAsync(int id) =>
        DeleteAsync("prepaidCards", id);

    // ── Persistent bill overrides (survive clearAll, cleared only on push success) ──
    public Task<List<PendingBillOverride>> GetPendingBillOverridesAsync() =>
        GetAllAsync<PendingBillOverride>("pendingBillOverrides");

    public Task SetBillOverrideAsync(int billId, bool isPaid) =>
        PutAsync("pendingBillOverrides", new PendingBillOverride { Id = billId, IsPaid = isPaid });

    public Task ClearBillOverrideAsync(int billId) =>
        DeleteAsync("pendingBillOverrides", billId);

    public async Task<List<PendingBillDelete>> GetPendingBillDeletesAsync()
    {
        try
        {
            return await GetAllAsync<PendingBillDelete>("billDeletes");
        }
        catch (JSException)
        {
            return new List<PendingBillDelete>();
        }
    }

    public async Task SetBillDeleteAsync(BillDelete deleted)
    {
        try
        {
            await PutAsync("billDeletes", PendingBillDelete.FromBillDelete(deleted));
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearBillDeleteAsync(int billId)
    {
        try
        {
            await DeleteAsync("billDeletes", billId);
        }
        catch (JSException)
        {
        }
    }

    // ── Persistent transaction edits (survive cloud replace until pushed) ─────
    public Task<List<PendingTransactionOverride>> GetPendingTransactionOverridesAsync() =>
        GetAllAsync<PendingTransactionOverride>("transactionOverrides");

    public Task SetTransactionOverrideAsync(Transaction transaction) =>
        PutAsync("transactionOverrides", new PendingTransactionOverride { Id = transaction.Id, Transaction = transaction });

    public Task ClearTransactionOverrideAsync(int transactionId) =>
        DeleteAsync("transactionOverrides", transactionId);

    public Task ClearTransactionOverridesAsync() =>
        ClearAsync("transactionOverrides");

    public Task<List<PendingTransactionDelete>> GetPendingTransactionDeletesAsync() =>
        GetAllAsync<PendingTransactionDelete>("transactionDeletes");

    public Task SetTransactionDeleteAsync(TransactionDelete deleted) =>
        PutAsync("transactionDeletes", new PendingTransactionDelete { Id = PendingTransactionDelete.GetStableId(deleted), Deleted = deleted });

    public Task ClearTransactionDeleteAsync(string id) =>
        DeleteAsync("transactionDeletes", id);

    public Task ClearTransactionDeletesAsync() =>
        ClearAsync("transactionDeletes");

    // ── Persistent setting edits (survive cloud replace until pushed) ─────────
    public Task<List<PendingSettingOverride>> GetPendingSettingOverridesAsync() =>
        GetAllAsync<PendingSettingOverride>("settingOverrides");

    public Task SetSettingOverrideAsync(AppSetting setting) =>
        PutAsync("settingOverrides", new PendingSettingOverride { Id = setting.Key, Setting = setting });

    public Task ClearSettingOverrideAsync(string key) =>
        DeleteAsync("settingOverrides", key);

    public Task ClearSettingOverridesAsync() =>
        ClearAsync("settingOverrides");

    public Task ClearBillOverridesAsync() =>
        ClearAsync("pendingBillOverrides");

    public async Task ClearBillDeletesAsync()
    {
        try
        {
            await ClearAsync("billDeletes");
        }
        catch (JSException)
        {
        }
    }

    public async Task<List<PendingDebtDelete>> GetPendingDebtDeletesAsync()
    {
        try
        {
            return await GetAllAsync<PendingDebtDelete>("debtDeletes");
        }
        catch (JSException)
        {
            return new List<PendingDebtDelete>();
        }
    }

    public async Task SetDebtDeleteAsync(Debt debt)
    {
        try
        {
            await PutAsync("debtDeletes", PendingDebtDelete.FromDebt(debt));
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearDebtDeleteAsync(int debtId)
    {
        try
        {
            await DeleteAsync("debtDeletes", debtId);
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearDebtDeletesAsync()
    {
        try
        {
            await ClearAsync("debtDeletes");
        }
        catch (JSException)
        {
        }
    }

    public async Task<List<PendingSavingsGoalDelete>> GetPendingSavingsGoalDeletesAsync()
    {
        try
        {
            return await GetAllAsync<PendingSavingsGoalDelete>("savingsGoalDeletes");
        }
        catch (JSException)
        {
            return new List<PendingSavingsGoalDelete>();
        }
    }

    public async Task SetSavingsGoalDeleteAsync(SavingsGoal goal)
    {
        try
        {
            await PutAsync("savingsGoalDeletes", PendingSavingsGoalDelete.FromSavingsGoal(goal));
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearSavingsGoalDeleteAsync(int goalId)
    {
        try
        {
            await DeleteAsync("savingsGoalDeletes", goalId);
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearSavingsGoalDeletesAsync()
    {
        try
        {
            await ClearAsync("savingsGoalDeletes");
        }
        catch (JSException)
        {
        }
    }

    public async Task<List<PendingTripDelete>> GetPendingTripDeletesAsync()
    {
        try
        {
            return await GetAllAsync<PendingTripDelete>("tripDeletes");
        }
        catch (JSException)
        {
            return new List<PendingTripDelete>();
        }
    }

    public async Task SetTripDeleteAsync(Trip trip)
    {
        try
        {
            await PutAsync("tripDeletes", PendingTripDelete.FromTrip(trip));
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearTripDeleteAsync(int tripId)
    {
        try
        {
            await DeleteAsync("tripDeletes", tripId);
        }
        catch (JSException)
        {
        }
    }

    public async Task ClearTripDeletesAsync()
    {
        try
        {
            await ClearAsync("tripDeletes");
        }
        catch (JSException)
        {
        }
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        var settings = await GetAppSettingsAsync();
        var existing = settings.FirstOrDefault(s => s.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
            await PutAsync("appSettings", existing);
        }
        else
        {
            var newId = settings.Count > 0 ? settings.Max(s => s.Id) + 1 : 1;
            await PutAsync("appSettings", new AppSetting { Id = newId, Key = key, Value = value });
        }
    }
}

public class PendingBillOverride
{
    public int Id { get; set; }   // = BillId (unique per bill)
    public bool IsPaid { get; set; }
}

public class PendingBillDelete
{
    public int Id { get; set; }   // = BillId
    public string Name { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public DateTime DueDate { get; set; }
    public BillFrequency Frequency { get; set; }

    public BillDelete ToBillDelete() => new()
    {
        Id = Id,
        Name = Name,
        AccountId = AccountId,
        AccountName = AccountName,
        AmountCents = AmountCents,
        DueDate = DueDate,
        Frequency = Frequency
    };

    public static PendingBillDelete FromBillDelete(BillDelete deleted) => new()
    {
        Id = deleted.Id,
        Name = deleted.Name,
        AccountId = deleted.AccountId,
        AccountName = deleted.AccountName,
        AmountCents = deleted.AmountCents,
        DueDate = deleted.DueDate,
        Frequency = deleted.Frequency
    };
}

public class PendingDebtDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BalanceCents { get; set; }
    public int OriginalBalanceCents { get; set; }

    public static PendingDebtDelete FromDebt(Debt debt) => new()
    {
        Id = debt.Id,
        Name = debt.Name,
        BalanceCents = debt.BalanceCents,
        OriginalBalanceCents = debt.OriginalBalanceCents
    };
}

public class PendingSavingsGoalDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TargetCents { get; set; }
    public int CurrentCents { get; set; }

    public static PendingSavingsGoalDelete FromSavingsGoal(SavingsGoal goal) => new()
    {
        Id = goal.Id,
        Name = goal.Name,
        TargetCents = goal.TargetCents,
        CurrentCents = goal.CurrentCents
    };
}

public class PendingTripDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Destination { get; set; }
    public DateTime? StartDate { get; set; }

    public static PendingTripDelete FromTrip(Trip trip) => new()
    {
        Id = trip.Id,
        Name = trip.Name,
        Destination = trip.Destination,
        StartDate = trip.StartDate
    };
}

public class PendingTransactionOverride
{
    public int Id { get; set; } // = Transaction Id
    public Transaction Transaction { get; set; } = new();
}

public class PendingSettingOverride
{
    public string Id { get; set; } = string.Empty; // = Setting Key
    public AppSetting Setting { get; set; } = new();
}

public class PendingTransactionDelete
{
    public string Id { get; set; } = string.Empty;
    public TransactionDelete Deleted { get; set; } = new();

    public static string GetStableId(TransactionDelete deleted) =>
        !string.IsNullOrWhiteSpace(deleted.UpTransactionId)
            ? $"up:{deleted.UpTransactionId}"
            : $"sig:{deleted.Date:yyyyMMdd}:{deleted.AmountCents}:{deleted.Description}".ToLowerInvariant();
}

public class LentTransaction
{
    public int Id { get; set; }          // = Transaction Id
    public string Note { get; set; } = "";  // e.g. "Jake's rego"
    public bool Repaid { get; set; }
    public int RepaidCents { get; set; }
    public decimal RepaidDollars
    {
        get => RepaidCents / 100m;
        set => RepaidCents = (int)Math.Round(value * 100m);
    }
    public DateTime MarkedAt { get; set; } = DateTime.Now;
}

public class SyncMeta
{
    public int Id { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string PcHost { get; set; } = string.Empty;
    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseKey { get; set; } = string.Empty;
}
