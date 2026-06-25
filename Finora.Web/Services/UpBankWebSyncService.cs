using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finora.Web.Models;

namespace Finora.Web.Services;

public class UpBankWebSyncService(HttpClient http, IndexedDbService db, SyncService sync)
{
    public const string AccessTokenSettingKey = "UpBankAccessToken";
    private const string LastSyncSettingKey = "UpBankPhoneLastSyncUtc";
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
            var debts = await db.GetDebtsAsync();
            var savingsGoals = await db.GetSavingsGoalsAsync();
            var weeklyBudgets = await db.GetWeeklyBudgetsAsync();
            var trips = await db.GetTripsAsync();
            var appSettings = await db.GetAppSettingsAsync();
            var transactionOverrides = await db.GetPendingTransactionOverridesAsync();
            var transactionDeletes = await db.GetPendingTransactionDeletesAsync();

            if (!HasPlanningData(bills, debts, savingsGoals, weeklyBudgets, trips) && (sync.HasCloudSync || sync.HasLocalSync))
            {
                await sync.AutoSyncAsync();
                accounts = await db.GetAccountsAsync();
                categories = await db.GetCategoriesAsync();
                transactions = await db.GetTransactionsAsync();
                bills = await db.GetBillsAsync();
                billStatuses = await db.GetBillStatusesAsync();
                debts = await db.GetDebtsAsync();
                savingsGoals = await db.GetSavingsGoalsAsync();
                weeklyBudgets = await db.GetWeeklyBudgetsAsync();
                trips = await db.GetTripsAsync();
                appSettings = await db.GetAppSettingsAsync();
                transactionOverrides = await db.GetPendingTransactionOverridesAsync();
                transactionDeletes = await db.GetPendingTransactionDeletesAsync();
            }

            var sinceUtc = DateTimeOffset.UtcNow.AddDays(-90);
            var newestSeenUtc = sinceUtc;
            var imported = 0;
            var balanceAdjustments = 0;
            var matchedBills = 0;
            var addedBalanceAdjustmentIds = new List<int>();
            // Phone-created records this pass — pushed to phone_push below so WPF
            // reconciles them into real SQLite IDs instead of them only living in
            // this device's local snapshot (see PushCurrentSnapshotToCloudAsync).
            var newTransactionsForPush = new List<Transaction>();
            var matchedBillStatusesForPush = new List<BillOccurrenceStatus>();

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var upAccounts = await FetchAccountsAsync();
            foreach (var upAccount in upAccounts)
            {
                GetOrCreateAccount(
                    accounts,
                    upAccount.Id,
                    GetLocalAccountName(upAccount.Attributes),
                    GetAccountType(upAccount.Attributes),
                    GetAccountColor(upAccount.Attributes));
            }

            var existingByUpId = transactions
                .Where(t => !string.IsNullOrWhiteSpace(t.UpTransactionId))
                .GroupBy(t => t.UpTransactionId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var url = $"{ApiBaseUrl}/transactions?page[size]=100&filter[since]={Uri.EscapeDataString(sinceUtc.ToString("O"))}";
            while (!string.IsNullOrWhiteSpace(url))
            {
                var page = await FetchTransactionsPageAsync(url);
                foreach (var upTransaction in page.Data)
                {
                    var occurredAt = upTransaction.Attributes.SettledAt ?? upTransaction.Attributes.CreatedAt;
                    if (occurredAt > newestSeenUtc) newestSeenUtc = occurredAt;

                    if (transactionDeletes.Any(d => string.Equals(d.Deleted.UpTransactionId, upTransaction.Id, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var amountCents = ParseAmountCents(upTransaction.Attributes.Amount.Value);
                    if (amountCents == 0) continue;

                    var account = GetTransactionAccount(accounts, upTransaction.Relationships.Account.Data?.Id);
                    var description = BuildDescription(upTransaction.Attributes);
                    var purchaseDate = DateOnlyUnspecified(ToUpBankLocalDateTime(upTransaction.Attributes.CreatedAt));
                    var occurredLocal = ToUpBankLocalDateTime(occurredAt);
                    if (transactionDeletes.Any(d => SameTransactionSignature(d.Deleted.Date, d.Deleted.Description, d.Deleted.AmountCents, purchaseDate, description, amountCents)))
                        continue;

                    var category = GetOrCreateCategory(
                        categories,
                        GetCategoryName(upTransaction.Relationships.Category.Data?.Id, description, amountCents),
                        amountCents > 0 ? CategoryType.Income : CategoryType.Expense);

                    if (existingByUpId.TryGetValue(upTransaction.Id, out var existingTransaction))
                    {
                        existingTransaction.Date = purchaseDate;
                        existingTransaction.Description = description;
                        existingTransaction.AmountCents = amountCents;
                        existingTransaction.AccountId = account.Id;
                        existingTransaction.CategoryId = category.Id;
                        existingTransaction.UpSettledAt = DateTime.SpecifyKind(occurredLocal, DateTimeKind.Unspecified);
                        await db.PutAsync("transactions", existingTransaction);
                        continue;
                    }

                    var transaction = new Transaction
                    {
                        Id = NextLocalId(transactions.Select(t => t.Id)),
                        Date = purchaseDate,
                        Description = description,
                        AmountCents = amountCents,
                        AccountId = account.Id,
                        CategoryId = category.Id,
                        UpTransactionId = upTransaction.Id,
                        UpSettledAt = DateTime.SpecifyKind(occurredLocal, DateTimeKind.Unspecified)
                    };

                    transactions.Add(transaction);
                    await db.PutAsync("transactions", transaction);
                    existingByUpId[upTransaction.Id] = transaction;
                    imported++;
                    newTransactionsForPush.Add(transaction);

                    if (amountCents < 0)
                    {
                        var matchedStatus = TryMarkMatchingBillPaid(bills, billStatuses, account.Id, purchaseDate, Math.Abs(amountCents), transaction.Id);
                        if (matchedStatus is not null)
                        {
                            matchedBills++;
                            matchedBillStatusesForPush.Add(matchedStatus);
                        }
                    }
                }

                url = page.Links.Next;
            }

            await RestoreOrphanedBillAdjustmentsAsync(categories, transactions, bills);

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
                addedBalanceAdjustmentIds.Add(adjustment.Id);
                newTransactionsForPush.Add(adjustment);
            }

            foreach (var account in accounts) await db.PutAsync("accounts", account);
            foreach (var category in categories) await db.PutAsync("categories", category);
            foreach (var bill in bills) await db.PutAsync("bills", bill);
            foreach (var status in billStatuses) await db.PutAsync("billOccurrenceStatuses", status);

            await ApplyPersistedTransactionChangesAsync(categories, transactions, transactionOverrides, transactionDeletes);
            balanceAdjustments = transactions.Count(t => addedBalanceAdjustmentIds.Contains(t.Id));

            await SaveSettingValueAsync(appSettings, LastSyncSettingKey, newestSeenUtc.ToUniversalTime().ToString("O"));

            // Without this, these new records only ever land in this device's local
            // snapshot and the direct finance_sync merge below — WPF never learns
            // about them via phone_push, so its next periodic push (every 5 min, or
            // on any local change) overwrites finance_sync with its own DB and wipes
            // them out of the cloud entirely.
            if (newTransactionsForPush.Count > 0 || matchedBillStatusesForPush.Count > 0)
            {
                var push = new PushPayload
                {
                    NewTransactions = newTransactionsForPush,
                    UpdatedBillStatuses = matchedBillStatusesForPush
                };
                if (sync.HasLocalSync) await sync.PushToPcAsync(push);
                if (sync.HasCloudSync) await sync.PushToSupabaseAsync(push);
            }

            var pushedSnapshot = await PushCurrentSnapshotToCloudAsync();

            return new UpBankWebSyncResult(imported, balanceAdjustments, matchedBills, pushedSnapshot);
        }
        catch (Exception ex)
        {
            LastError = BuildErrorMessage(ex);
            return null;
        }
        finally
        {
            IsSyncing = false;
            OnStateChanged?.Invoke();
        }
    }

    // HttpRequestException.Message is built from a System.Net.Http resource
    // string that gets trimmed in Blazor WASM, leaving the raw resource key
    // (e.g. "Net_http_message_not_success_statuscode_reason,401,Unauthorized")
    // instead of a readable message. Build our own message from StatusCode.
    private static string BuildErrorMessage(Exception ex)
    {
        if (ex is HttpRequestException http)
        {
            if (http.StatusCode == HttpStatusCode.Unauthorized)
                return "Your Up Bank token is invalid or has expired. Add a new token in Settings.";
            if (http.StatusCode is { } code)
                return $"Up Bank returned HTTP {(int)code} ({code}).";
        }
        return ex.Message.Length > 160 ? ex.Message[..160] : ex.Message;
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

    private async Task ApplyPersistedTransactionChangesAsync(
        List<Category> categories,
        List<Transaction> transactions,
        List<PendingTransactionOverride> overrides,
        List<PendingTransactionDelete> deletes)
    {
        foreach (var ov in overrides)
        {
            var updated = ov.Transaction;
            var transaction = FindTransaction(transactions, updated);
            if (transaction is null) continue;

            transaction.Date = DateOnlyUnspecified(updated.Date);
            transaction.Description = updated.Description;
            transaction.AmountCents = updated.AmountCents;
            transaction.AccountId = updated.AccountId;
            transaction.CategoryId = ResolveCategory(categories, updated.CategoryName, updated.CategoryId, updated.AmountCents).Id;
            transaction.TransferId = updated.TransferId;
            transaction.UpTransactionId = updated.UpTransactionId;
            transaction.IsUnnecessary = updated.IsUnnecessary;
            await db.PutAsync("transactions", transaction);
        }

        foreach (var deleted in deletes.Select(d => d.Deleted))
        {
            var transaction = FindTransaction(transactions, deleted);
            if (transaction is null) continue;
            if (IsGeneratedBalanceAdjustment(transaction)) continue;

            transactions.Remove(transaction);
            await db.DeleteAsync("transactions", transaction.Id);
        }
    }

    private async Task RestoreOrphanedBillAdjustmentsAsync(
        List<Category> categories,
        List<Transaction> transactions,
        List<Bill> bills)
    {
        var balanceAdjustmentCategory = GetOrCreateCategory(categories, "Balance Adjustment", CategoryType.Expense);
        foreach (var transaction in transactions.Where(IsOrphanedBillAdjustmentCandidate).ToList())
        {
            var transactionKey = GetMerchantKey(transaction.Description);
            var stillHasMatchingBill = !string.IsNullOrWhiteSpace(transactionKey) &&
                bills.Any(b =>
                    b.AccountId == transaction.AccountId &&
                    Math.Abs(b.AmountCents - Math.Abs(transaction.AmountCents)) <= 1 &&
                    string.Equals(GetMerchantKey(b.Name), transactionKey, StringComparison.OrdinalIgnoreCase));
            if (stillHasMatchingBill)
            {
                continue;
            }

            transaction.Description = "Up balance adjustment";
            transaction.CategoryId = balanceAdjustmentCategory.Id;
            transaction.CategoryName = balanceAdjustmentCategory.Name;
            transaction.TransferId = Guid.Empty;
            await db.PutAsync("transactions", transaction);
        }
    }

    private static bool IsOrphanedBillAdjustmentCandidate(Transaction transaction) =>
        transaction.AmountCents < 0 &&
        transaction.UpTransactionId is null &&
        transaction.TransferId is null &&
        !string.IsNullOrWhiteSpace(transaction.AccountName) &&
        string.Equals(transaction.CategoryName, transaction.AccountName, StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedBalanceAdjustment(Transaction transaction) =>
        transaction.UpTransactionId is null &&
        (transaction.TransferId == Guid.Empty ||
            transaction.Description.Equals("Up balance adjustment", StringComparison.OrdinalIgnoreCase) ||
            transaction.CategoryName.Equals("Balance Adjustment", StringComparison.OrdinalIgnoreCase));

    private static string GetMerchantKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static Transaction? FindTransaction(List<Transaction> transactions, Transaction updated)
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

    private static Transaction? FindTransaction(List<Transaction> transactions, TransactionDelete deleted)
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

    private static Category ResolveCategory(List<Category> categories, string? categoryName, int categoryId, int amountCents)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var byName = categories.FirstOrDefault(c => string.Equals(c.Name, categoryName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }

        var byId = categories.FirstOrDefault(c => c.Id == categoryId);
        if (byId is not null) return byId;

        var fallbackName = amountCents > 0 ? "Income" : "Misc";
        var fallback = categories.FirstOrDefault(c => string.Equals(c.Name, fallbackName, StringComparison.OrdinalIgnoreCase));
        if (fallback is not null) return fallback;

        var created = new Category
        {
            Id = NextLocalId(categories.Select(c => c.Id)),
            Name = fallbackName,
            Type = amountCents > 0 ? CategoryType.Income : CategoryType.Expense
        };
        categories.Add(created);
        return created;
    }

    private async Task<bool> PushCurrentSnapshotToCloudAsync()
    {
        if (!sync.HasCloudSync) return false;

        var payload = new SyncPayload
        {
            Accounts = await db.GetAccountsAsync(),
            Categories = await db.GetCategoriesAsync(),
            Transactions = await db.GetTransactionsAsync(),
            Bills = await db.GetBillsAsync(),
            BillOccurrenceStatuses = await db.GetBillStatusesAsync(),
            Debts = await db.GetDebtsAsync(),
            DebtPayments = await db.GetDebtPaymentsAsync(),
            SavingsGoals = await db.GetSavingsGoalsAsync(),
            WeeklyBudgets = await db.GetWeeklyBudgetsAsync(),
            Trips = await db.GetTripsAsync(),
            AppSettings = await db.GetAppSettingsAsync(),
            SyncedAt = DateTime.UtcNow
        };

        if (!HasPlanningData(payload))
        {
            var cloud = await sync.FetchCloudPayloadAsync();
            if (cloud is not null && HasPlanningData(cloud))
            {
                payload.Bills = cloud.Bills;
                payload.BillOccurrenceStatuses = cloud.BillOccurrenceStatuses;
                payload.Debts = cloud.Debts;
                payload.DebtPayments = cloud.DebtPayments;
                payload.SavingsGoals = cloud.SavingsGoals;
                payload.WeeklyBudgets = cloud.WeeklyBudgets;
                payload.Trips = cloud.Trips;
            }
        }

        var pushed = await sync.PushFullSyncAsync(payload);
        if (!pushed)
        {
            LastError = sync.LastError;
        }
        return pushed;
    }

    private static bool HasPlanningData(SyncPayload payload) =>
        HasPlanningData(payload.Bills, payload.Debts, payload.SavingsGoals, payload.WeeklyBudgets, payload.Trips);

    private static bool HasPlanningData(
        IReadOnlyCollection<Bill>? bills,
        IReadOnlyCollection<Debt>? debts,
        IReadOnlyCollection<SavingsGoal>? savingsGoals,
        IReadOnlyCollection<WeeklyBudget>? weeklyBudgets,
        IReadOnlyCollection<Trip>? trips) =>
        (bills?.Count ?? 0) > 0 ||
        (debts?.Count ?? 0) > 0 ||
        (savingsGoals?.Count ?? 0) > 0 ||
        (weeklyBudgets?.Count ?? 0) > 0 ||
        (trips?.Count ?? 0) > 0;

    private BillOccurrenceStatus? TryMarkMatchingBillPaid(List<Bill> bills, List<BillOccurrenceStatus> statuses, int accountId, DateTime paidOn, int amountCents, int transactionId)
    {
        var candidates = bills
            .Select(b => new { Bill = b, DueDate = GetClosestBillDueDate(b, paidOn) })
            .Where(x => x.Bill.AccountId == accountId
                        && Math.Abs(x.Bill.AmountCents - amountCents) <= 1
                        && Math.Abs((x.DueDate.Date - paidOn.Date).TotalDays) <= 5
                        && !IsBillOccurrencePaid(statuses, x.Bill.Id, x.DueDate))
            .OrderBy(x => Math.Abs((x.DueDate.Date - paidOn.Date).TotalDays))
            .ToList();

        if (candidates.Count != 1) return null;

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
        return status;
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

    private static Account GetTransactionAccount(List<Account> accounts, string? upAccountId)
    {
        if (!string.IsNullOrWhiteSpace(upAccountId))
        {
            var linked = accounts.FirstOrDefault(a => a.UpAccountId == upAccountId);
            if (linked is not null) return linked;
        }

        return accounts.FirstOrDefault(a => a.Type == AccountType.Spending)
            ?? GetOrCreateAccount(accounts, null, "Spending", AccountType.Spending, "#16A34A");
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

    private static DateTime ToUpBankLocalDateTime(DateTimeOffset value)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
            return TimeZoneInfo.ConvertTime(value, zone).DateTime;
        }
        catch
        {
            return value.LocalDateTime;
        }
    }

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
        public UpAccountRelationship Account { get; set; } = new();
        public UpCategoryRelationship Category { get; set; } = new();
    }

    private sealed class UpAccountRelationship
    {
        public UpRelationshipData? Data { get; set; }
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

public record UpBankWebSyncResult(int ImportedTransactions, int BalanceAdjustments, int MatchedBills, bool CloudSnapshotPushed);
