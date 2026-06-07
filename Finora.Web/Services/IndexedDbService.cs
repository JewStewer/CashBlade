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
        // Read config BEFORE clearing so we don't lose PC host / Supabase credentials
        var existingMeta = await GetSyncMetaAsync();
        var meta = existingMeta.FirstOrDefault() ?? new SyncMeta { Id = 1 };

        await ClearAllAsync();

        await PutBulkAsync("accounts", p.Accounts);
        await PutBulkAsync("categories", p.Categories);
        await PutBulkAsync("transactions", p.Transactions);
        await PutBulkAsync("bills", p.Bills);
        await PutBulkAsync("billOccurrenceStatuses", p.BillOccurrenceStatuses);
        await PutBulkAsync("debts", p.Debts);
        await PutBulkAsync("debtPayments", p.DebtPayments);
        await PutBulkAsync("savingsGoals", p.SavingsGoals);
        await PutBulkAsync("weeklyBudgets", p.WeeklyBudgets);
        await PutBulkAsync("appSettings", p.AppSettings);

        // Restore config with updated sync timestamp
        meta.LastSyncedAt = p.SyncedAt;
        await PutAsync("syncMeta", meta);
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        var settings = await GetAppSettingsAsync();
        return settings.FirstOrDefault(s => s.Key == key)?.Value;
    }

    // ── Persistent bill overrides (survive clearAll, cleared only on push success) ──
    public Task<List<PendingBillOverride>> GetPendingBillOverridesAsync() =>
        GetAllAsync<PendingBillOverride>("pendingBillOverrides");

    public Task SetBillOverrideAsync(int billId, bool isPaid) =>
        PutAsync("pendingBillOverrides", new PendingBillOverride { Id = billId, IsPaid = isPaid });

    public Task ClearBillOverrideAsync(int billId) =>
        DeleteAsync("pendingBillOverrides", billId);

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

public class SyncMeta
{
    public int Id { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string PcHost { get; set; } = string.Empty;
    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseKey { get; set; } = string.Empty;
}
