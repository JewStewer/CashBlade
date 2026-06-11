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

    public async Task SaveSyncPayloadAsync(SyncPayload p)
    {
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
            syncMeta               = meta
        };

        var json    = JsonSerializer.Serialize(replacePayload, _opts);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        await js.InvokeVoidAsync("db.replaceAll", element);
    }

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

    // ── Persistent bill overrides (survive clearAll, cleared only on push success) ──
    public Task<List<PendingBillOverride>> GetPendingBillOverridesAsync() =>
        GetAllAsync<PendingBillOverride>("pendingBillOverrides");

    public Task SetBillOverrideAsync(int billId, bool isPaid) =>
        PutAsync("pendingBillOverrides", new PendingBillOverride { Id = billId, IsPaid = isPaid });

    public Task ClearBillOverrideAsync(int billId) =>
        DeleteAsync("pendingBillOverrides", billId);

    public Task<List<PendingBillDelete>> GetPendingBillDeletesAsync() =>
        GetAllAsync<PendingBillDelete>("billDeletes");

    public Task SetBillDeleteAsync(int billId) =>
        PutAsync("billDeletes", new PendingBillDelete { Id = billId });

    public Task ClearBillDeleteAsync(int billId) =>
        DeleteAsync("billDeletes", billId);

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

    public Task ClearTransactionDeletesAsync() =>
        ClearAsync("transactionDeletes");

    public Task ClearBillOverridesAsync() =>
        ClearAsync("pendingBillOverrides");

    public Task ClearBillDeletesAsync() =>
        ClearAsync("billDeletes");

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
}

public class PendingTransactionOverride
{
    public int Id { get; set; } // = Transaction Id
    public Transaction Transaction { get; set; } = new();
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
