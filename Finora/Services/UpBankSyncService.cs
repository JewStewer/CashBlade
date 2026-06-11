using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finora.Data;
using Finora.Models;
using Microsoft.EntityFrameworkCore;

namespace Finora.Services;

public class UpBankSyncService
{
    public const string AccessTokenSettingKey = "UpBankAccessToken";
    private const string LastSyncSettingKey = "UpBankLastSyncUtc";
    private const string LastCategoryBackfillSettingKey = "UpBankLastCategoryBackfillUtc";
    private static readonly TimeSpan CategoryBackfillInterval = TimeSpan.FromHours(24);
    private const string ApiBaseUrl = "https://api.up.com.au/api/v1";
    private const string UpAccountName = "Spending";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool HasAccessToken()
    {
        using var db = new FinoraDbContext();
        return db.AppSettings.Any(s => s.Key == AccessTokenSettingKey && s.Value != "");
    }

    public string GetAccessToken()
    {
        using var db = new FinoraDbContext();
        return db.AppSettings
            .Where(s => s.Key == AccessTokenSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? "";
    }

    public void SaveAccessToken(string accessToken)
    {
        using var db = new FinoraDbContext();
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == AccessTokenSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = AccessTokenSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = accessToken.Trim();
        db.SaveChanges();
    }

    public async Task<UpBankSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Add your Up Bank API token before syncing.");
        }

        using var db = new FinoraDbContext();
        var sinceUtc = GetLastSyncUtc(db) ?? DateTimeOffset.UtcNow.AddDays(-90);

        // Re-scanning the last 90 days on every sync (so categories the user fixes in
        // the Up app later get backfilled here too) is what makes routine syncs slow.
        // Only widen the window for that backfill once a day; otherwise just fetch
        // what's new since the last sync.
        var lastBackfillUtc = GetLastCategoryBackfillUtc(db);
        var dueForCategoryBackfill = lastBackfillUtc is null || DateTimeOffset.UtcNow - lastBackfillUtc.Value >= CategoryBackfillInterval;
        if (dueForCategoryBackfill)
        {
            var categoryBackfillSinceUtc = DateTimeOffset.UtcNow.AddDays(-90);
            if (sinceUtc > categoryBackfillSinceUtc)
            {
                sinceUtc = categoryBackfillSinceUtc;
            }
        }
        var imported = 0;
        var debtPayments = 0;
        var accountBalanceAdjustments = 0;
        var renamedBillAdjustments = 0;
        var ambiguousBillMatches = 0;
        var newestSeenUtc = sinceUtc;

        // Preload Up-linked transactions once instead of one query per transaction below —
        // the dominant per-row cost during the wider backfill pass.
        var existingByUpId = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.UpTransactionId != null)
            .ToDictionary(t => t.UpTransactionId!, t => t);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = $"{ApiBaseUrl}/transactions?page[size]=100&filter[since]={Uri.EscapeDataString(sinceUtc.ToString("O"))}";
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<UpTransactionsPage>(stream, JsonOptions, cancellationToken)
                ?? new UpTransactionsPage();

            foreach (var upTransaction in page.Data)
            {
                if (!string.Equals(upTransaction.Attributes.Status, "SETTLED", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var occurredAt = upTransaction.Attributes.SettledAt ?? upTransaction.Attributes.CreatedAt;
                if (occurredAt > newestSeenUtc)
                {
                    newestSeenUtc = occurredAt;
                }

                var amountCents = ParseAmountCents(upTransaction.Attributes.Amount.Value);
                if (amountCents == 0)
                {
                    continue;
                }

                if (existingByUpId.TryGetValue(upTransaction.Id, out var existingTransaction))
                {
                    ApplyUpCategoryToExistingTransaction(db, existingTransaction, upTransaction.Relationships.Category.Data?.Id, amountCents);
                    continue;
                }

                var description = BuildDescription(upTransaction.Attributes);
                var debt = amountCents < 0 ? DebtPaymentMatcher.FindMatchingDebt(db, description) : null;
                var category = debt is not null
                    ? GetCategory(db, "Debt")
                    : GetUpCategory(db, upTransaction.Relationships.Category.Data?.Id, amountCents)
                        ?? GetLearnedCategory(db, description, amountCents)
                        ?? GetCategory(db, GetCategoryName(description, amountCents, false));
                var account = GetOrCreateUpAccount(db);

                // Use CreatedAt (when the user made the purchase) rather than SettledAt
                // (when the bank processed it) for the displayed date.  Settlement can happen
                // overnight UTC — a 9 am AEST purchase settles at 2 am UTC the next day,
                // which converts back to a June-9 AEST date even though the user made it on June-8.
                var purchaseDate = upTransaction.Attributes.CreatedAt.LocalDateTime.Date;

                var newTransaction = new Transaction
                {
                    Date = purchaseDate,
                    Description = description,
                    AmountCents = amountCents,
                    Account = account,
                    Category = category,
                    TransferId = null,
                    UpTransactionId = upTransaction.Id
                };
                db.Transactions.Add(newTransaction);
                existingByUpId[upTransaction.Id] = newTransaction;

                imported++;

                if (debt is not null)
                {
                    var paymentCents = Math.Abs(amountCents);
                    debt.BalanceCents = Math.Max(0, debt.BalanceCents - paymentCents);
                    db.DebtPayments.Add(new DebtPayment
                    {
                        Debt = debt,
                        UpTransactionId = upTransaction.Id,
                        AmountCents = paymentCents,
                        PaidOn = purchaseDate,
                        Description = description
                    });
                    debtPayments++;
                }
            }

            url = page.Links.Next;
        }

        await db.SaveChangesAsync(cancellationToken);
        RestoreOrphanedBillAdjustments(db);
        var balanceSync = await SyncAccountBalancesAsync(http, db, cancellationToken);
        accountBalanceAdjustments = balanceSync.Adjustments;
        renamedBillAdjustments = balanceSync.RenamedBillAdjustments;
        ambiguousBillMatches = balanceSync.AmbiguousBillMatches;
        SetLastSyncUtc(db, newestSeenUtc);
        if (dueForCategoryBackfill)
        {
            SetLastCategoryBackfillUtc(db, DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken);

        return new UpBankSyncResult(imported, debtPayments, accountBalanceAdjustments, renamedBillAdjustments, ambiguousBillMatches);
    }

    private static void RestoreOrphanedBillAdjustments(FinoraDbContext db)
    {
        var balanceAdjustmentCategory = GetCategory(db, "Balance Adjustment");
        var candidateTransactions = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t =>
                t.AmountCents < 0 &&
                t.UpTransactionId == null &&
                t.TransferId == null &&
                t.AccountId > 0)
            .ToList();

        foreach (var transaction in candidateTransactions)
        {
            var accountName = transaction.Account?.Name ?? string.Empty;
            var categoryName = transaction.Category?.Name ?? string.Empty;
            if (!string.Equals(categoryName, accountName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var transactionKey = TransactionClassification.GetMerchantKey(transaction.Description);
            var stillHasMatchingBill = !string.IsNullOrWhiteSpace(transactionKey) &&
                db.Bills
                    .AsEnumerable()
                    .Any(b =>
                        b.AccountId == transaction.AccountId &&
                        Math.Abs(b.AmountCents - Math.Abs(transaction.AmountCents)) <= 1 &&
                        string.Equals(TransactionClassification.GetMerchantKey(b.Name), transactionKey, StringComparison.OrdinalIgnoreCase));
            if (stillHasMatchingBill)
            {
                continue;
            }

            transaction.Description = "Up balance adjustment";
            transaction.Category = balanceAdjustmentCategory;
            transaction.TransferId = Guid.Empty;
        }
    }

    private static async Task<(int Adjustments, int RenamedBillAdjustments, int AmbiguousBillMatches)> SyncAccountBalancesAsync(HttpClient http, FinoraDbContext db, CancellationToken cancellationToken)
    {
        var adjustments = 0;
        var renamedBillAdjustments = 0;
        var ambiguousBillMatches = 0;
        var url = $"{ApiBaseUrl}/accounts?page[size]=100";
        var seenUpAccountIds = new HashSet<string>();

        while (!string.IsNullOrWhiteSpace(url))
        {
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<UpAccountsPage>(stream, JsonOptions, cancellationToken)
                ?? new UpAccountsPage();

            foreach (var upAccount in page.Data)
            {
                seenUpAccountIds.Add(upAccount.Id);
                var accountName = GetLocalAccountName(upAccount.Attributes);
                var account = GetOrCreateAccount(db, upAccount.Id, accountName, GetAccountType(upAccount.Attributes), GetAccountColor(upAccount.Attributes));
                var upBalanceCents = ParseAmountCents(upAccount.Attributes.Balance.Value);
                var currentBalanceCents = account.Id == 0
                    ? 0
                    : db.Transactions.Where(t => t.AccountId == account.Id).Sum(t => t.AmountCents);
                var adjustmentCents = upBalanceCents - currentBalanceCents;

                if (adjustmentCents == 0)
                {
                    continue;
                }

                var candidates = adjustmentCents < 0
                    ? FindMatchingUnpaidBills(db, account, DateTime.Today, Math.Abs(adjustmentCents))
                    : new List<(Bill Bill, DateTime DueDate)>();
                var matchedBill = candidates.Count == 1 ? candidates[0].Bill : null;
                var description = matchedBill is null
                    ? "Up balance adjustment"
                    : matchedBill.Name;
                // Always "Balance Adjustment" so this reconciliation entry is treated as an
                // internal movement and excluded from spending totals — even when it's named
                // after a matched bill, the bill's actual payment is already counted via the
                // real Up Bank transaction on the paying account.
                var category = GetCategory(db, "Balance Adjustment");

                db.Transactions.Add(new Transaction
                {
                    Date = DateTime.Today,
                    Description = description,
                    AmountCents = adjustmentCents,
                    Account = account,
                    Category = category,
                    TransferId = Guid.Empty
                });
                if (matchedBill is not null)
                {
                    var dueDate = candidates[0].DueDate;
                    MarkBillOccurrencePaid(db, matchedBill.Id, dueDate, null, $"Matched by {account.Name} account, amount, and balance sync");
                    renamedBillAdjustments++;
                }
                else if (candidates.Count > 1)
                {
                    ambiguousBillMatches++;
                }

                adjustments++;
            }

            url = page.Links.Next;
        }

        // Remove local accounts whose Up Bank account no longer exists in the API
        RemoveDeletedUpAccounts(db, seenUpAccountIds);

        return (adjustments, renamedBillAdjustments, ambiguousBillMatches);
    }

    private static void RemoveDeletedUpAccounts(FinoraDbContext db, HashSet<string> seenUpAccountIds)
    {
        // Find local accounts linked to Up Bank that are no longer returned by the API
        var staleAccounts = db.Accounts
            .Where(a => a.UpAccountId != null && a.UpAccountId != "")
            .AsEnumerable()
            .Where(a => !seenUpAccountIds.Contains(a.UpAccountId!))
            .ToList();

        foreach (var account in staleAccounts)
        {
            var transactions = db.Transactions.Where(t => t.AccountId == account.Id).ToList();
            var hasBills = db.Bills.Any(b => b.AccountId == account.Id);

            // If the only transactions are Up Bank sync artifacts (balance adjustments),
            // delete them along with the account — they're not real user data
            var hasRealTransactions = transactions.Any(t =>
                t.Description != "Up balance adjustment" &&
                t.UpTransactionId == null);

            if (!hasRealTransactions && !hasBills)
            {
                // Delete sync artifact transactions then the account itself
                db.Transactions.RemoveRange(transactions);
                db.Accounts.Remove(account);
            }
            else
            {
                // Has real transaction history — keep the account as a manual account
                // so historical data is not lost, but clear the Up Bank link
                account.UpAccountId = null;
            }
        }
    }

    private static List<(Bill Bill, DateTime DueDate)> FindMatchingUnpaidBills(FinoraDbContext db, Account account, DateTime date, int amountCents)
    {
        return db.Bills
            .AsEnumerable()
            .Select(b => new { Bill = b, DueDate = GetClosestBillDueDate(b, date) })
            .Where(match =>
                match.Bill.AccountId == account.Id &&
                Math.Abs(match.Bill.AmountCents - amountCents) <= 1 &&
                Math.Abs((match.DueDate.Date - date.Date).TotalDays) <= 5 &&
                !IsBillOccurrencePaid(db, match.Bill.Id, match.DueDate))
            .OrderBy(match => Math.Abs((match.DueDate.Date - date.Date).TotalDays))
            .Select(match => (match.Bill, match.DueDate))
            .ToList();
    }

    private static DateTime GetClosestBillDueDate(Bill bill, DateTime date)
    {
        var dueDate = bill.DueDate.Date;
        while (dueDate < date.Date.AddDays(-5))
        {
            dueDate = GetNextBillDueDate(dueDate, bill.Frequency);
        }

        var nextDueDate = GetNextBillDueDate(dueDate, bill.Frequency);
        return Math.Abs((nextDueDate.Date - date.Date).TotalDays) < Math.Abs((dueDate.Date - date.Date).TotalDays)
            ? nextDueDate
            : dueDate;
    }

    private static DateTime GetNextBillDueDate(DateTime dueDate, BillFrequency frequency)
    {
        return frequency switch
        {
            BillFrequency.Weekly => dueDate.AddDays(7),
            BillFrequency.Fortnightly => dueDate.AddDays(14),
            BillFrequency.Monthly => dueDate.AddMonths(1),
            BillFrequency.Quarterly => dueDate.AddMonths(3),
            BillFrequency.Yearly => dueDate.AddYears(1),
            _ => dueDate.AddMonths(1)
        };
    }

    private static bool IsBillOccurrencePaid(FinoraDbContext db, int billId, DateTime dueDate)
    {
        return db.BillOccurrenceStatuses
            .Where(s => s.BillId == billId && s.DueDate == dueDate.Date)
            .Select(s => s.IsPaid)
            .FirstOrDefault();
    }

    private static void MarkBillOccurrencePaid(FinoraDbContext db, int billId, DateTime dueDate, int? transactionId, string matchNote)
    {
        var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == billId && s.DueDate == dueDate.Date);
        if (status is null)
        {
            status = new BillOccurrenceStatus
            {
                BillId = billId,
                DueDate = dueDate.Date
            };
            db.BillOccurrenceStatuses.Add(status);
        }

        status.IsPaid = true;
        status.PaidOn = DateTime.Today;
        status.MatchedTransactionId = transactionId;
        status.MatchNote = matchNote;

        // Advance bill.DueDate to the next occurrence — mirrors the manual "Mark Paid"
        // behaviour in MainWindow and MainViewModel so the bill shows correctly in the
        // next billing cycle on both the WPF and the phone app.
        // Only advance if the bill's stored DueDate is at (or before) this occurrence;
        // avoids double-advancing when the user already marked it paid manually.
        var bill = db.Bills.Find(billId);
        if (bill is not null && bill.DueDate.Date <= dueDate.Date.AddDays(1))
        {
            bill.IsPaid = false;  // reset for next cycle
            bill.DueDate = GetNextBillDueDate(dueDate, bill.Frequency);
        }
    }

    private static DateTimeOffset? GetLastSyncUtc(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == LastSyncSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault();

        return DateTimeOffset.TryParse(value, out var lastSync)
            ? lastSync.ToUniversalTime()
            : null;
    }

    private static void SetLastSyncUtc(FinoraDbContext db, DateTimeOffset lastSyncUtc)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == LastSyncSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = LastSyncSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = lastSyncUtc.ToUniversalTime().ToString("O");
    }

    private static DateTimeOffset? GetLastCategoryBackfillUtc(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == LastCategoryBackfillSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault();

        return DateTimeOffset.TryParse(value, out var lastBackfill)
            ? lastBackfill.ToUniversalTime()
            : null;
    }

    private static void SetLastCategoryBackfillUtc(FinoraDbContext db, DateTimeOffset lastBackfillUtc)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == LastCategoryBackfillSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = LastCategoryBackfillSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = lastBackfillUtc.ToUniversalTime().ToString("O");
    }

    private static int ParseAmountCents(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars)
            ? (int)Math.Round(dollars * 100m)
            : 0;
    }

    private static string BuildDescription(UpTransactionAttributes attributes)
    {
        var parts = new[] { attributes.Description, attributes.Message, attributes.RawText }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count == 0 ? "Up Bank transaction" : string.Join(" - ", parts);
    }

    private static string GetCategoryName(string description, int amountCents, bool isDebtPayment)
    {
        if (isDebtPayment)
        {
            return "Debt";
        }

        if (TransactionClassification.IsInternalMovementDescription(description))
        {
            return "Transfer";
        }

        return amountCents > 0 ? "Income" : "Misc";
    }

    private static Category? GetUpCategory(FinoraDbContext db, string? upCategoryId, int amountCents)
    {
        if (amountCents >= 0 || string.IsNullOrWhiteSpace(upCategoryId))
        {
            return null;
        }

        return GetCategory(db, FormatUpCategoryName(upCategoryId));
    }

    private static void ApplyUpCategoryToExistingTransaction(FinoraDbContext db, Transaction transaction, string? upCategoryId, int amountCents)
    {
        var upCategory = GetUpCategory(db, upCategoryId, amountCents);
        if (upCategory is null || transaction.Category?.Name is not ("Misc" or "Unplanned"))
        {
            return;
        }

        transaction.Category = upCategory;
    }

    private static string FormatUpCategoryName(string upCategoryId)
    {
        return string.Join(" ", upCategoryId
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word)));
    }

    private static Account GetOrCreateUpAccount(FinoraDbContext db)
    {
        return GetOrCreateAccount(db, null, UpAccountName, AccountType.Spending, "#16A34A");
    }

    private static Account GetOrCreateAccount(FinoraDbContext db, string? upAccountId, string name, AccountType type, string colorHex)
    {
        var pendingAccount = db.Accounts.Local.FirstOrDefault(a =>
            IsMatchingUpAccount(a, upAccountId) || string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (pendingAccount is not null)
        {
            UpdateAccountFromUp(db, pendingAccount, upAccountId, name, type, colorHex);
            return pendingAccount;
        }

        var account = !string.IsNullOrWhiteSpace(upAccountId)
            ? db.Accounts.FirstOrDefault(a => a.UpAccountId == upAccountId)
            : null;

        account ??= db.Accounts.FirstOrDefault(a => a.Name == name);

        if (account is not null)
        {
            UpdateAccountFromUp(db, account, upAccountId, name, type, colorHex);
            return account;
        }

        account = new Account
        {
            Name = name,
            UpAccountId = upAccountId,
            Type = type,
            ColorHex = colorHex
        };
        db.Accounts.Add(account);
        return account;
    }

    private static bool IsMatchingUpAccount(Account account, string? upAccountId)
    {
        return !string.IsNullOrWhiteSpace(upAccountId) && account.UpAccountId == upAccountId;
    }

    private static void UpdateAccountFromUp(FinoraDbContext db, Account account, string? upAccountId, string name, AccountType type, string colorHex)
    {
        RemoveStaleNameConflict(db, account, name);

        if (!string.IsNullOrWhiteSpace(upAccountId))
        {
            account.UpAccountId = upAccountId;
        }

        account.Name = name;
        // Do NOT overwrite Type — the user may have changed it in Edit Account.
        // Type is only set when the account is first created from Up Bank.
        account.ColorHex = colorHex;
    }

    private static void RemoveStaleNameConflict(FinoraDbContext db, Account account, string name)
    {
        var conflict = db.Accounts.FirstOrDefault(a => a.Id != account.Id && a.Name == name);
        if (conflict is null)
        {
            return;
        }

        var hasBills = db.Bills.Any(b => b.AccountId == conflict.Id);
        var hasNonAdjustmentTransactions = db.Transactions.Any(t => t.AccountId == conflict.Id && t.Description != "Up balance adjustment");
        if (hasBills || hasNonAdjustmentTransactions)
        {
            return;
        }

        var adjustmentTransactions = db.Transactions
            .Where(t => t.AccountId == conflict.Id && t.Description == "Up balance adjustment")
            .ToList();
        db.Transactions.RemoveRange(adjustmentTransactions);
        db.Accounts.Remove(conflict);
    }

    private static Category GetCategory(FinoraDbContext db, string name)
    {
        var pendingCategory = db.Categories.Local.FirstOrDefault(c => c.Name == name);
        if (pendingCategory is not null)
        {
            return pendingCategory;
        }

        var category = db.Categories.FirstOrDefault(c => c.Name == name);
        if (category is not null)
        {
            return category;
        }

        category = new Category
        {
            Name = name,
            Type = name == "Income" ? CategoryType.Income : CategoryType.Expense
        };
        db.Categories.Add(category);
        return category;
    }

    private static Category? GetLearnedCategory(FinoraDbContext db, string description, int amountCents)
    {
        var merchantKey = TransactionClassification.GetMerchantKey(description);
        if (merchantKey == string.Empty)
        {
            return null;
        }

        var isExpense = amountCents < 0;
        return db.Transactions
            .Include(t => t.Category)
            .AsEnumerable()
            .Where(t => isExpense ? t.AmountCents < 0 : t.AmountCents > 0)
            .Where(t => t.Category is not null && t.Category.Name is not ("Misc" or "Unplanned"))
            .Where(t => TransactionClassification.GetMerchantKey(t.Description) == merchantKey)
            .GroupBy(t => t.Category!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static string GetLocalAccountName(UpAccountAttributes attributes)
    {
        return string.Equals(attributes.AccountType, "TRANSACTIONAL", StringComparison.OrdinalIgnoreCase)
            ? UpAccountName
            : string.IsNullOrWhiteSpace(attributes.DisplayName) ? attributes.Name : attributes.DisplayName;
    }

    private static AccountType GetAccountType(UpAccountAttributes attributes)
    {
        return string.Equals(attributes.AccountType, "SAVER", StringComparison.OrdinalIgnoreCase)
            ? AccountType.Savings
            : AccountType.Spending;
    }

    private static string GetAccountColor(UpAccountAttributes attributes)
    {
        return string.Equals(attributes.AccountType, "SAVER", StringComparison.OrdinalIgnoreCase)
            ? "#7C3AED"
            : "#16A34A";
    }

    private sealed class UpAccountsPage
    {
        public List<UpAccount> Data { get; set; } = new();

        public UpLinks Links { get; set; } = new();
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

    private sealed class UpTransactionsPage
    {
        public List<UpTransaction> Data { get; set; } = new();

        public UpLinks Links { get; set; } = new();
    }

    private sealed class UpLinks
    {
        public string? Next { get; set; }
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
