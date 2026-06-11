using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finora.Web.Models;

namespace Finora.Web.Services;

public class UpBankWebSyncService(HttpClient http, IndexedDbService db)
{
    public const string AccessTokenSettingKey = "UpBankAccessToken";
    private const string LastSyncSettingKey = "UpBankLastSyncUtc";
    private const string ApiBaseUrl = "https://api.up.com.au/api/v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? LastError { get; private set; }
    public bool IsSyncing { get; private set; }

    public event Action? OnStateChanged;

    public async Task<string> GetAccessTokenAsync() =>
        await db.GetSettingAsync(AccessTokenSettingKey) ?? string.Empty;

    public async Task SaveAccessTokenAsync(string token) =>
        await db.SaveSettingAsync(AccessTokenSettingKey, token.Trim());

    public async Task<UpBankWebSyncResult?> SyncAsync()
    {
        var token = await GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            LastError = "Add your Up Bank API token before syncing.";
            OnStateChanged?.Invoke();
            return null;
        }

        IsSyncing = true;
        LastError = null;
        OnStateChanged?.Invoke();

        try
        {
            var accounts = await db.GetAccountsAsync();
            var categories = await db.GetCategoriesAsync();
            var transactions = await db.GetTransactionsAsync();
            var bills = await db.GetBillsAsync();
            var billStatuses = await db.GetBillStatusesAsync();
            var appSettings = await db.GetAppSettingsAsync();

            var sinceUtc = GetLastSyncUtc(appSettings) ?? DateTimeOffset.UtcNow.AddDays(-90);
            var newestSeenUtc = sinceUtc;
            var imported = 0;
            var balanceAdjustments = 0;
            var matchedBills = 0;

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var upAccounts = await FetchAccountsAsync();
            foreach (var upAccount in upAccounts)
            {
                var account = GetOrCreateAccount(
                    accounts,
                    upAccount.Id,
                    GetLocalAccountName(upAccount.Attributes),
                    GetAccountType(upAccount.Attributes),
                    GetAccountColor(upAccount.Attributes));

                var upBalanceCents = ParseAmountCents(upAccount.Attributes.Balance.Value);
                var currentBalanceCents = transactions.Where(t => t.AccountId == account.Id).Sum(t => t.AmountCents);
                var adjustmentCents = upBalanceCents - currentBalanceCents;
                if (adjustmentCents == 0) continue;

                var category = GetOrCreateCategory(categories, "Balance Adjustment", CategoryType.Expense);
                var adjustment = new Transaction
                {
                    Id = NextLocalId(transactions.Select(t => t.Id)),
                    Date = TodayUnspecified(),
                    Description = "Up balance adjustment",
                    AmountCents = adjustmentCents,
                    AccountId = account.Id,
                    CategoryId = category.Id,
                    TransferId = Guid.Empty
                };
                transactions.Add(adjustment);
                await db.PutAsync("transactions", adjustment);
                balanceAdjustments++;
            }

            var existingUpIds = transactions
                .Where(t => !string.IsNullOrWhiteSpace(t.UpTransactionId))
                .Select(t => t.UpTransactionId!)
                .ToHashSet(StringComparer.Ordinal);

            var url = $"{ApiBaseUrl}/transactions?page[size]=100&filter[since]={Uri.EscapeDataString(sinceUtc.ToString("O"))}";
            while (!string.IsNullOrWhiteSpace(url))
            {
                var page = await FetchTransactionsPageAsync(url);
                foreach (var upTransaction in page.Data)
                {
                    if (!string.Equals(upTransaction.Attributes.Status, "SETTLED", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var occurredAt = upTransaction.Attributes.SettledAt ?? upTransaction.Attributes.CreatedAt;
                    if (occurredAt > newestSeenUtc) newestSeenUtc = occurredAt;

                    if (existingUpIds.Contains(upTransaction.Id)) continue;

                    var amountCents = ParseAmountCents(upTransaction.Attributes.Amount.Value);
                    if (amountCents == 0) continue;

                    var account = accounts.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.UpAccountId))
                        ?? GetOrCreateAccount(accounts, null, "Spending", AccountType.Spending, "#16A34A");
                    var description = BuildDescription(upTransaction.Attributes);
                    var category = GetOrCreateCategory(
                        categories,
                        GetCategoryName(upTransaction.Relationships.Category.Data?.Id, description, amountCents),
                        amountCents > 0 ? CategoryType.Income : CategoryType.Expense);
                    var purchaseDate = DateOnlyUnspecified(upTransaction.Attributes.CreatedAt.LocalDateTime);

                    var transaction = new Transaction
                    {
                        Id = NextLocalId(transactions.Select(t => t.Id)),
                        Date = purchaseDate,
                        Description = description,
                        AmountCents = amountCents,
                        AccountId = account.Id,
                        CategoryId = category.Id,
                        UpTransactionId = upTransaction.Id
                    };

                    transactions.Add(transaction);
                    await db.PutAsync("transactions", transaction);
                    existingUpIds.Add(upTransaction.Id);
                    imported++;

                    if (amountCents < 0 && TryMarkMatchingBillPaid(bills, billStatuses, account.Id, purchaseDate, Math.Abs(amountCents), transaction.Id))
                        matchedBills++;
                }

                url = page.Links.Next;
            }

            foreach (var account in accounts) await db.PutAsync("accounts", account);
            foreach (var category in categories) await db.PutAsync("categories", category);
            foreach (var bill in bills) await db.PutAsync("bills", bill);
            foreach (var status in billStatuses) await db.PutAsync("billOccurrenceStatuses", status);

            await SaveSettingValueAsync(appSettings, LastSyncSettingKey, newestSeenUtc.ToUniversalTime().ToString("O"));

            return new UpBankWebSyncResult(imported, balanceAdjustments, matchedBills);
        }
        catch (Exception ex)
        {
            LastError = ex.Message.Length > 160 ? ex.Message[..160] : ex.Message;
            return null;
        }
        finally
        {
            IsSyncing = false;
            OnStateChanged?.Invoke();
        }
    }

    private async Task<List<UpAccount>> FetchAccountsAsync()
    {
        var accounts = new List<UpAccount>();
        var url = $"{ApiBaseUrl}/accounts?page[size]=100";
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var page = await JsonSerializer.DeserializeAsync<UpAccountsPage>(stream, JsonOptions) ?? new UpAccountsPage();
            accounts.AddRange(page.Data);
            url = page.Links.Next;
        }
        return accounts;
    }

    private async Task<UpTransactionsPage> FetchTransactionsPageAsync(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<UpTransactionsPage>(stream, JsonOptions) ?? new UpTransactionsPage();
    }

    private async Task SaveSettingValueAsync(List<AppSetting> settings, string key, string value)
    {
        await db.SaveSettingAsync(key, value);
        var setting = settings.FirstOrDefault(s => s.Key == key);
        if (setting is null) settings.Add(new AppSetting { Id = NextLocalId(settings.Select(s => s.Id)), Key = key, Value = value });
        else setting.Value = value;
    }

    private bool TryMarkMatchingBillPaid(List<Bill> bills, List<BillOccurrenceStatus> statuses, int accountId, DateTime paidOn, int amountCents, int transactionId)
    {
        var candidates = bills
            .Select(b => new { Bill = b, DueDate = GetClosestBillDueDate(b, paidOn) })
            .Where(x => x.Bill.AccountId == accountId
                        && Math.Abs(x.Bill.AmountCents - amountCents) <= 1
                        && Math.Abs((x.DueDate.Date - paidOn.Date).TotalDays) <= 5
                        && !IsBillOccurrencePaid(statuses, x.Bill.Id, x.DueDate))
            .OrderBy(x => Math.Abs((x.DueDate.Date - paidOn.Date).TotalDays))
            .ToList();

        if (candidates.Count != 1) return false;

        var match = candidates[0];
        var status = statuses.FirstOrDefault(s => s.BillId == match.Bill.Id && s.DueDate.Date == match.DueDate.Date);
        if (status is null)
        {
            status = new BillOccurrenceStatus
            {
                Id = NextLocalId(statuses.Select(s => s.Id)),
                BillId = match.Bill.Id,
                DueDate = match.DueDate.Date
            };
            statuses.Add(status);
        }

        status.IsPaid = true;
        status.PaidOn = paidOn.Date;
        status.MatchedTransactionId = transactionId;
        status.MatchNote = "Matched from Up Bank sync on iPhone";
        match.Bill.IsPaid = true;
        return true;
    }

    private static bool IsBillOccurrencePaid(List<BillOccurrenceStatus> statuses, int billId, DateTime dueDate) =>
        statuses.Any(s => s.BillId == billId && s.DueDate.Date == dueDate.Date && s.IsPaid);

    private static Account GetOrCreateAccount(List<Account> accounts, string? upAccountId, string name, AccountType type, string colorHex)
    {
        var account = !string.IsNullOrWhiteSpace(upAccountId)
            ? accounts.FirstOrDefault(a => a.UpAccountId == upAccountId)
            : null;
        account ??= accounts.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            account = new Account { Id = NextLocalId(accounts.Select(a => a.Id)), Name = name, UpAccountId = upAccountId, Type = type, ColorHex = colorHex };
            accounts.Add(account);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(upAccountId)) account.UpAccountId = upAccountId;
            account.Name = name;
            account.ColorHex = colorHex;
        }
        return account;
    }

    private static Category GetOrCreateCategory(List<Category> categories, string name, CategoryType type)
    {
        var category = categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (category is not null) return category;

        category = new Category { Id = NextLocalId(categories.Select(c => c.Id)), Name = name, Type = type };
        categories.Add(category);
        return category;
    }

    private static DateTimeOffset? GetLastSyncUtc(List<AppSetting> settings)
    {
        var value = settings.FirstOrDefault(s => s.Key == LastSyncSettingKey)?.Value;
        return DateTimeOffset.TryParse(value, out var lastSync) ? lastSync.ToUniversalTime() : null;
    }

    private static DateTime GetClosestBillDueDate(Bill bill, DateTime date)
    {
        var dueDate = bill.DueDate.Date;
        while (dueDate < date.Date.AddDays(-5)) dueDate = AdvanceDueDate(dueDate, bill.Frequency);
        var nextDueDate = AdvanceDueDate(dueDate, bill.Frequency);
        return Math.Abs((nextDueDate.Date - date.Date).TotalDays) < Math.Abs((dueDate.Date - date.Date).TotalDays)
            ? nextDueDate
            : dueDate;
    }

    private static DateTime AdvanceDueDate(DateTime dueDate, BillFrequency frequency) => frequency switch
    {
        BillFrequency.Weekly => dueDate.AddDays(7),
        BillFrequency.Fortnightly => dueDate.AddDays(14),
        BillFrequency.Monthly => dueDate.AddMonths(1),
        BillFrequency.Quarterly => dueDate.AddMonths(3),
        BillFrequency.Yearly => dueDate.AddYears(1),
        _ => dueDate.AddMonths(1)
    };

    private static int NextLocalId(IEnumerable<int> ids)
    {
        var min = ids.DefaultIfEmpty(0).Min();
        return Math.Min(min - 1, -1);
    }

    private static DateTime TodayUnspecified() => DateOnlyUnspecified(DateTime.Today);

    private static DateTime DateOnlyUnspecified(DateTime value) =>
        new(value.Year, value.Month, value.Day);

    private static int ParseAmountCents(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars)
            ? (int)Math.Round(dollars * 100m)
            : 0;

    private static string BuildDescription(UpTransactionAttributes attributes)
    {
        var parts = new[] { attributes.Description, attributes.Message, attributes.RawText }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count == 0 ? "Up Bank transaction" : string.Join(" - ", parts);
    }

    private static string GetCategoryName(string? upCategoryId, string description, int amountCents)
    {
        if (amountCents > 0) return "Income";
        if (!string.IsNullOrWhiteSpace(upCategoryId)) return FormatUpCategoryName(upCategoryId);
        if (description.Contains("transfer", StringComparison.OrdinalIgnoreCase)) return "Transfer";
        return "Misc";
    }

    private static string FormatUpCategoryName(string upCategoryId) =>
        string.Join(" ", upCategoryId
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word)));

    private static string GetLocalAccountName(UpAccountAttributes attributes) =>
        string.Equals(attributes.AccountType, "TRANSACTIONAL", StringComparison.OrdinalIgnoreCase)
            ? "Spending"
            : string.IsNullOrWhiteSpace(attributes.DisplayName) ? attributes.Name : attributes.DisplayName;

    private static AccountType GetAccountType(UpAccountAttributes attributes) =>
        string.Equals(attributes.AccountType, "SAVER", StringComparison.OrdinalIgnoreCase)
            ? AccountType.Savings
            : AccountType.Spending;

    private static string GetAccountColor(UpAccountAttributes attributes) =>
        string.Equals(attributes.AccountType, "SAVER", StringComparison.OrdinalIgnoreCase) ? "#7C3AED" : "#16A34A";

    private sealed class UpAccountsPage
    {
        public List<UpAccount> Data { get; set; } = new();
        public UpLinks Links { get; set; } = new();
    }

    private sealed class UpTransactionsPage
    {
        public List<UpTransaction> Data { get; set; } = new();
        public UpLinks Links { get; set; } = new();
    }

    private sealed class UpLinks
    {
        public string? Next { get; set; }
    }

    private sealed class UpAccount
    {
        public string Id { get; set; } = string.Empty;
        public UpAccountAttributes Attributes { get; set; } = new();
    }

    private sealed class UpAccountAttributes
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public UpMoney Balance { get; set; } = new();
    }

    private sealed class UpTransaction
    {
        public string Id { get; set; } = string.Empty;
        public UpTransactionAttributes Attributes { get; set; } = new();
        public UpTransactionRelationships Relationships { get; set; } = new();
    }

    private sealed class UpTransactionAttributes
    {
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? RawText { get; set; }
        public UpMoney Amount { get; set; } = new();
        public DateTimeOffset? SettledAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class UpTransactionRelationships
    {
        public UpCategoryRelationship Category { get; set; } = new();
    }

    private sealed class UpCategoryRelationship
    {
        public UpRelationshipData? Data { get; set; }
    }

    private sealed class UpRelationshipData
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class UpMoney
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = "0.00";
    }
}

public record UpBankWebSyncResult(int ImportedTransactions, int BalanceAdjustments, int MatchedBills);
