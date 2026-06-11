using Finora.Web.Models;

namespace Finora.Web.Services;

public class AppState(IndexedDbService db, SyncService sync)
{
    // ── Raw data ──────────────────────────────────────────────────────────────
    public List<Account> Accounts { get; private set; } = new();
    public List<Category> Categories { get; private set; } = new();
    public List<Transaction> Transactions { get; private set; } = new();
    public List<Bill> Bills { get; private set; } = new();
    public List<BillOccurrenceStatus> BillStatuses { get; private set; } = new();
    public List<Debt> Debts { get; private set; } = new();
    public List<SavingsGoal> SavingsGoals { get; private set; } = new();
    public List<WeeklyBudget> WeeklyBudgets { get; private set; } = new();
    public List<AppSetting> AppSettings { get; private set; } = new();
    // Phone-only: transactions marked as lent (excluded from spending until repaid)
    public List<LentTransaction> LentTransactions { get; private set; } = new();
    private HashSet<int> _unrepaidLentIds = new();
    private HashSet<int> _matchedInternalMovementIds = new();

    // ── Computed summaries ────────────────────────────────────────────────────
    public decimal TotalBalance { get; private set; }
    public decimal SavingsTotal { get; private set; }
    public decimal DebtTotal { get; private set; }
    public decimal NetWorth { get; private set; }
    public decimal WeeklyIncome { get; private set; }
    public decimal BudgetBills { get; private set; }
    public decimal BudgetEssentials { get; private set; }
    public decimal BudgetSavings { get; private set; }
    public decimal BudgetUnplanned { get; private set; }
    public decimal BudgetLeftover => WeeklyIncome - BudgetBills - BudgetEssentials - BudgetSavings - BudgetUnplanned;
    public decimal SafeToSpendAmount => Math.Max(BudgetLeftover, 0);

    // ── Settings ──────────────────────────────────────────────────────────────
    public DateTime NextPayDate { get; private set; } = DateTime.Today;
    public string SummaryPeriod { get; private set; } = "Monthly";

    // ── Pending phone-side changes (synced on next push) ──────────────────────
    private readonly List<Transaction> _pendingNewTransactions = new();
    private readonly List<Transaction> _pendingUpdatedTransactions = new();
    private readonly List<int> _pendingDeletedTransactionIds = new();
    private readonly List<BillOccurrenceStatus> _pendingBillStatuses = new();
    private readonly List<Bill> _pendingNewBills = new();
    private readonly List<Bill> _pendingUpdatedBills = new();
    private readonly List<AppSetting> _pendingUpdatedSettings = new();

    // Debounce + re-entrancy guard for ScheduleSyncSoon/SyncAndReloadAsync —
    // iOS suspends a backgrounded PWA almost immediately, so the 5-minute
    // periodic timer rarely survives long enough to push an edit on its own.
    private CancellationTokenSource? _syncDebounceCts;
    private bool _syncInProgress;

    public bool HasPendingChanges =>
        _pendingNewTransactions.Count > 0 ||
        _pendingUpdatedTransactions.Count > 0 ||
        _pendingDeletedTransactionIds.Count > 0 ||
        _pendingBillStatuses.Count > 0 ||
        _pendingNewBills.Count > 0 ||
        _pendingUpdatedBills.Count > 0 ||
        _pendingUpdatedSettings.Count > 0;

    public event Action? OnChange;

    // ── Account balances computed from transactions ───────────────────────────
    public Dictionary<int, decimal> AccountBalances { get; private set; } = new();

    // ── Bills for current period ──────────────────────────────────────────────
    public List<Bill> BillsDueBeforePayday { get; private set; } = new();
    public decimal TotalBillsDue { get; private set; }

    // ── Transactions for display (most recent 100) ────────────────────────────
    public List<Transaction> RecentTransactions { get; private set; } = new();

    public bool IsLoaded { get; private set; }

    public async Task LoadAsync()
    {
        Accounts = await db.GetAccountsAsync();
        Categories = await db.GetCategoriesAsync();
        Transactions = await db.GetTransactionsAsync();
        Bills = await db.GetBillsAsync();
        BillStatuses = await db.GetBillStatusesAsync();
        Debts = await db.GetDebtsAsync();
        SavingsGoals = await db.GetSavingsGoalsAsync();
        WeeklyBudgets = await db.GetWeeklyBudgetsAsync();
        AppSettings = await db.GetAppSettingsAsync();
        LentTransactions = await db.GetLentTransactionsAsync();
        _unrepaidLentIds = LentTransactions.Where(l => !l.Repaid).Select(l => l.Id).ToHashSet();

        // Apply any phone-side overrides that survived a cloud replace or app restart.
        await ApplyPersistedTransactionOverridesAsync();
        await ApplyPersistedBillOverridesAsync();

        await sync.InitAsync();
        Compute();
        IsLoaded = true;
        OnChange?.Invoke();
    }

    // ── Lent money tracking ──────────────────────────────────────────────────
    public bool IsLent(int txnId) => LentTransactions.Any(l => l.Id == txnId);
    public bool IsUnrepaid(int txnId) => _unrepaidLentIds.Contains(txnId);

    public async Task MarkLentAsync(int txnId, string note)
    {
        LentTransactions.RemoveAll(l => l.Id == txnId);
        var lent = new LentTransaction { Id = txnId, Note = note, Repaid = false, MarkedAt = DateTime.Now };
        LentTransactions.Add(lent);
        _unrepaidLentIds.Add(txnId);
        await db.SetLentTransactionAsync(lent);
        OnChange?.Invoke();
    }

    public async Task UnmarkLentAsync(int txnId)
    {
        LentTransactions.RemoveAll(l => l.Id == txnId);
        _unrepaidLentIds.Remove(txnId);
        await db.DeleteLentTransactionAsync(txnId);
        OnChange?.Invoke();
    }

    public async Task MarkRepaidAsync(int txnId)
    {
        var lent = LentTransactions.FirstOrDefault(l => l.Id == txnId);
        if (lent is null) return;
        lent.Repaid = true;
        _unrepaidLentIds.Remove(txnId);
        await db.SetLentTransactionAsync(lent);
        OnChange?.Invoke();
    }

    private async Task ApplyPersistedBillOverridesAsync()
    {
        var overrides = await db.GetPendingBillOverridesAsync();
        foreach (var ov in overrides)
        {
            var bill = Bills.FirstOrDefault(b => b.Id == ov.Id);
            if (bill is null) continue;
            bill.IsPaid = ov.IsPaid;

            var status = GetOrCreateCurrentStatus(bill);
            status.IsPaid = ov.IsPaid;
            status.PaidOn = ov.IsPaid ? (status.PaidOn ?? DateTime.Now) : null;
        }
    }

    private async Task ApplyPersistedTransactionOverridesAsync()
    {
        var overrides = await db.GetPendingTransactionOverridesAsync();
        foreach (var ov in overrides)
        {
            var updated = ov.Transaction;
            var transaction = Transactions.FirstOrDefault(t => t.Id == updated.Id);
            if (transaction is null) continue;

            ApplyTransactionEdit(transaction, updated);
            await db.PutAsync("transactions", transaction);
            QueueUpdatedTransaction(transaction);
        }
    }

    private void Compute()
    {
        ComputeSettings();
        DenormaliseTransactions();
        ComputeMatchedInternalMovements();
        DenormaliseBills();
        ComputeBalances();
        ComputeBudget();
        ComputeSummaries();
        DetectPayAccount();
        ComputeBillsDue();
        RecentTransactions = Transactions
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToList();
    }

    private void ComputeSettings()
    {
        var npd = GetSetting("NextPayDate");
        if (npd is not null && DateTime.TryParse(npd, out var dt))
            NextPayDate = dt.Date;
        else
            NextPayDate = DateTime.Today;

        SummaryPeriod = GetSetting("SummaryPeriod") ?? "Monthly";

        var budget = WeeklyBudgets.FirstOrDefault();
        if (budget is not null)
        {
            WeeklyIncome = budget.IncomeDollars;
            BudgetBills = budget.BillsDollars;
            BudgetEssentials = budget.EssentialsDollars;
            BudgetSavings = budget.SavingsDollars;
            BudgetUnplanned = budget.UnplannedDollars;
        }
    }

    private void DenormaliseTransactions()
    {
        var accountMap = Accounts.ToDictionary(a => a.Id, a => a.Name);
        var catMap = Categories.ToDictionary(c => c.Id, c => c.Name);
        foreach (var t in Transactions)
        {
            t.AccountName = accountMap.GetValueOrDefault(t.AccountId, "");
            t.CategoryName = catMap.GetValueOrDefault(t.CategoryId, "");
        }
    }

    private void ComputeMatchedInternalMovements()
    {
        _matchedInternalMovementIds = new HashSet<int>();

        var candidates = Transactions
            .Where(t => t.AmountCents != 0
                        && !TransactionClassification.IsInternalMovementCategory(t.CategoryName)
                        && !TransactionClassification.HasLinkedTransferId(t)
                        && TransactionClassification.IsInternalMovementDescription(t.Description))
            .GroupBy(t => new { Date = t.Date.Date, AbsAmount = Math.Abs(t.AmountCents) });

        foreach (var group in candidates)
        {
            var outgoing = group.Where(t => t.AmountCents < 0).OrderBy(t => t.Id).ToList();
            var incoming = group.Where(t => t.AmountCents > 0).OrderBy(t => t.Id).ToList();

            foreach (var debit in outgoing)
            {
                var credit = incoming.FirstOrDefault(t => t.AccountId != debit.AccountId);
                if (credit is null) continue;

                _matchedInternalMovementIds.Add(debit.Id);
                _matchedInternalMovementIds.Add(credit.Id);
                incoming.Remove(credit);
            }
        }
    }

    private void DenormaliseBills()
    {
        var accountMap = Accounts.ToDictionary(a => a.Id, a => a.Name);
        foreach (var b in Bills)
        {
            b.AccountName = accountMap.GetValueOrDefault(b.AccountId, "");
            b.EffectiveDueDate = GetEffectiveDueDate(b);
        }
    }

    // ── Due-date helpers ──────────────────────────────────────────────────────
    /// <summary>
    /// Advances bill.DueDate by the bill's frequency until it reaches the most
    /// recent past (or today's) occurrence — the current billing cycle.
    /// Required because Up Bank auto-payment matching records a BillOccurrenceStatus
    /// without advancing bill.DueDate; this compensates on the phone side.
    /// </summary>
    private static DateTime GetEffectiveDueDate(Bill bill)
    {
        var dueDate = bill.DueDate.Date;
        var today   = DateTime.Today;
        // Advance until we reach the current or next upcoming occurrence.
        // We deliberately advance PAST today — we want the upcoming cycle, not
        // the most-recently-passed one, so unpaid June bills aren't hidden behind
        // a "paid in May" status.
        while (dueDate < today)
            dueDate = AdvanceDueDate(dueDate, bill.Frequency);
        return dueDate;
    }

    private static DateTime AdvanceDueDate(DateTime d, BillFrequency f) => f switch
    {
        BillFrequency.Weekly      => d.AddDays(7),
        BillFrequency.Fortnightly => d.AddDays(14),
        BillFrequency.Monthly     => d.AddMonths(1),
        BillFrequency.Quarterly   => d.AddMonths(3),
        BillFrequency.Yearly      => d.AddYears(1),
        _                         => d.AddMonths(1)
    };

    // Find (or create) the BillOccurrenceStatus for a bill's current billing
    // cycle, keyed by EffectiveDueDate to match IsBillPaid's primary check —
    // otherwise a status keyed on the (possibly stale) bill.DueDate never
    // matches and IsBillPaid falls through to "effectiveDue > DueDate → false".
    private BillOccurrenceStatus GetOrCreateCurrentStatus(Bill bill)
    {
        var effectiveDue = bill.EffectiveDueDate == default ? GetEffectiveDueDate(bill) : bill.EffectiveDueDate;
        var status = BillStatuses.FirstOrDefault(s => s.BillId == bill.Id && s.DueDate.Date == effectiveDue.Date);
        if (status is null)
        {
            status = new BillOccurrenceStatus { Id = NextLocalId(BillStatuses.Select(s => s.Id)), BillId = bill.Id, DueDate = effectiveDue };
            BillStatuses.Add(status);
        }
        return status;
    }

    private static int NextLocalId(IEnumerable<int> ids)
    {
        var min = ids.DefaultIfEmpty(0).Min();
        return Math.Min(min - 1, -1);
    }

    private void ComputeBalances()
    {
        AccountBalances = Transactions
            .GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.AmountDollars));

        TotalBalance = AccountBalances.Values.Sum();
        SavingsTotal = Accounts
            .Where(a => a.Type is AccountType.Savings)
            .Sum(a => AccountBalances.GetValueOrDefault(a.Id));
        DebtTotal = Debts.Sum(d => d.BalanceDollars);
        NetWorth = TotalBalance - DebtTotal;
    }

    private void ComputeBudget()
    {
        // Already loaded from WeeklyBudgets in ComputeSettings
    }

    private void ComputeSummaries()
    {
        // e.g. recent spending stats could go here
    }

    private void ComputeBillsDue()
    {
        var today  = DateTime.Today;
        var payEnd = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);

        // Use EffectiveDueDate so bills whose DueDate wasn't advanced by auto-matching
        // still appear when their current cycle falls before the next payday.
        BillsDueBeforePayday = Bills
            .Where(b => !IsBillPaid(b) && b.EffectiveDueDate.Date <= payEnd)
            .OrderBy(b => b.EffectiveDueDate)
            .ToList();

        TotalBillsDue = BillsDueBeforePayday.Sum(b => b.AmountDollars);
    }

    public string? GetSetting(string key) =>
        AppSettings.FirstOrDefault(s => s.Key == key)?.Value;

    // ── Computed properties for Dashboard ─────────────────────────────────────
    public decimal GetAccountBalance(int accountId) =>
        AccountBalances.GetValueOrDefault(accountId);

    public int PayAccountId { get; private set; }
    public decimal EstimatedPayAmount { get; private set; }
    public bool HasPayEstimate => EstimatedPayAmount > 0;

    public bool IsBillPaid(Bill bill)
    {
        // Use the pre-computed effective due date (current billing cycle).
        // If not yet computed (called before DenormaliseBills), compute inline.
        var effectiveDue = bill.EffectiveDueDate == default
            ? GetEffectiveDueDate(bill)
            : bill.EffectiveDueDate;

        // 1. Exact status for the current-cycle due date is the most authoritative.
        var exactStatus = BillStatuses
            .FirstOrDefault(s => s.BillId == bill.Id && s.DueDate.Date == effectiveDue.Date);
        if (exactStatus is not null) return exactStatus.IsPaid;

        // 2. If the effective date has advanced past the stored DueDate, the bill has
        //    cycled into a new period with no status yet → it is NOT paid.
        //    (Happens when Up Bank auto-match set status for an old cycle without
        //    advancing bill.DueDate to the next cycle.)
        if (effectiveDue.Date > bill.DueDate.Date) return false;

        // 3. Tight date-drift fallback: a status within ±3 days of the effective due
        //    date covers cases where the PC recorded a slightly different date (e.g.
        //    same-day payment).  Intentionally narrow so a previous cycle's payment
        //    (e.g. paid June 2, due June 9) is NOT treated as "paid for this cycle".
        var latest = BillStatuses
            .Where(s => s.BillId == bill.Id)
            .OrderByDescending(s => s.DueDate)
            .FirstOrDefault();
        if (latest is not null)
        {
            var daysFromEffective = Math.Abs((latest.DueDate.Date - effectiveDue.Date).TotalDays);
            if (daysFromEffective <= 3) return latest.IsPaid;
        }

        // 4. No status history (or too far from effective date) — fall back to
        //    bill.IsPaid.  Never check this before the status checks; the flag is
        //    never auto-reset between billing cycles and goes stale.
        return bill.IsPaid;
    }

    public List<Transaction> GetTransactionsForPeriod(DateTime from, DateTime to) =>
        Transactions.Where(t => t.Date.Date >= from && t.Date.Date <= to).ToList();

    public (DateTime from, DateTime to) GetCurrentPeriod()
    {
        if (SummaryPeriod == "Weekly")
        {
            var mon = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + (int)DayOfWeek.Monday);
            if (DateTime.Today.DayOfWeek == DayOfWeek.Sunday) mon = mon.AddDays(-7);
            return (mon, mon.AddDays(6));
        }
        return (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
                new DateTime(DateTime.Today.Year, DateTime.Today.Month,
                    DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month)));
    }

    // ── Week spending helpers ──────────────────────────────────────────────────
    /// <summary>Total spending for the ISO week N weeks ago (0 = current week).</summary>
    public decimal GetWeekSpending(int weeksAgo = 0)
    {
        var today = DateTime.Today;
        var dow = (int)today.DayOfWeek;
        var monday = today.AddDays(-(dow == 0 ? 6 : dow - 1));
        var from = monday.AddDays(-7 * weeksAgo);
        var to = from.AddDays(6);
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0 && !IsInternalMovement(t))
            .Sum(t => Math.Abs(t.AmountDollars));
    }

    /// <summary>Average daily spending over the last N days (excluding today if partial).</summary>
    public decimal GetAvgDailySpending(int days = 30)
    {
        var from = DateTime.Today.AddDays(-(days - 1));
        var total = Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date < DateTime.Today && t.AmountCents < 0 && !IsInternalMovement(t))
            .Sum(t => Math.Abs(t.AmountDollars));
        return days > 1 ? Math.Round(total / (days - 1), 2) : 0m;
    }

    /// <summary>All transactions for a specific date, in display order.</summary>
    public List<Transaction> GetTransactionsForDate(DateTime date) =>
        Transactions
            .Where(t => t.Date.Date == date.Date)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToList();

    // ── Spending stats ─────────────────────────────────────────────────────────
    // Total spending (all categories) — used for dashboard/trend
    public decimal GetPeriodSpending()
    {
        var (from, to) = GetCurrentPeriod();
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsInternalMovement(t) && !_unrepaidLentIds.Contains(t.Id))
            .Sum(t => Math.Abs(t.AmountDollars));
    }

    // Discretionary spending = total minus bill categories (matches WPF budget comparison)
    public decimal GetDiscretionarySpending()
    {
        var (from, to) = GetCurrentPeriod();
        return GetDiscretionarySpendingForPeriod(from, to);
    }

    public decimal GetDiscretionarySpendingForPeriod(DateTime from, DateTime to) =>
        Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsInternalMovement(t) && !IsBudgetedBillTransaction(t)
                        && !_unrepaidLentIds.Contains(t.Id))
            .Sum(t => Math.Abs(t.AmountDollars));

    // Weekly discretionary budget = essentials + unplanned
    public decimal DiscretionaryBudget => BudgetEssentials + BudgetUnplanned;

    public decimal GetTodaySpending() =>
        Transactions
            .Where(t => t.Date.Date == DateTime.Today && t.AmountCents < 0 && !IsInternalMovement(t))
            .Sum(t => Math.Abs(t.AmountDollars));

    public List<(string Category, decimal Amount)> GetTopCategories(int n = 5, bool excludeBills = false)
    {
        var (from, to) = GetCurrentPeriod();
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsInternalMovement(t) && (!excludeBills || !IsBudgetedBillTransaction(t)))
            .GroupBy(t => t.CategoryName)
            .Select(g => (g.Key, g.Sum(t => Math.Abs(t.AmountDollars))))
            .OrderByDescending(x => x.Item2)
            .Take(n)
            .ToList();
    }

    public string VapidPublicKey =>
        AppSettings.FirstOrDefault(s => s.Key == "VapidPublicKey")?.Value ?? string.Empty;

    public decimal GetCategoryLimitDollars(string categoryName)
    {
        var val = AppSettings.FirstOrDefault(s => s.Key == $"CategoryLimit:{categoryName}")?.Value;
        return int.TryParse(val, out var cents) ? cents / 100m : 0m;
    }

    public List<Bill> GetUpcomingBills(int days = 3) =>
        Bills.Where(b => !IsBillPaid(b) &&
                         b.EffectiveDueDate.Date >= DateTime.Today &&
                         b.EffectiveDueDate.Date <= DateTime.Today.AddDays(days))
             .OrderBy(b => b.EffectiveDueDate)
             .ToList();

    public List<(string Month, decimal Income, decimal Spending)> GetMonthlyTrend(int months = 6)
    {
        var result = new List<(string, decimal, decimal)>();
        for (int i = months - 1; i >= 0; i--)
        {
            var d = DateTime.Today.AddMonths(-i);
            var from = new DateTime(d.Year, d.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var txs = Transactions.Where(t => t.Date >= from && t.Date <= to && !IsInternalMovement(t)).ToList();
            var inc = txs.Where(t => t.AmountCents > 0).Sum(t => t.AmountDollars);
            var spend = txs.Where(t => t.AmountCents < 0).Sum(t => Math.Abs(t.AmountDollars));
            result.Add((d.ToString("MMM"), inc, spend));
        }
        return result;
    }

    private void DetectPayAccount()
    {
        var payTxns = Transactions
            .Where(t => t.AmountCents >= 20000 && !IsInternalMovement(t))
            .OrderByDescending(t => t.Date)
            .Take(3)
            .ToList();
        var mostRecent = payTxns.FirstOrDefault();
        PayAccountId = mostRecent?.AccountId
            ?? Accounts.FirstOrDefault(a => a.Type == AccountType.Spending)?.Id
            ?? 0;
        EstimatedPayAmount = payTxns.Count > 0
            ? payTxns.Average(t => t.AmountDollars)
            : WeeklyIncome;
    }

    public (decimal Now, decimal AfterBills, decimal AfterPay) GetForecastTotals()
    {
        var today      = DateTime.Today;
        var payEnd     = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);
        var billsTotal = Bills.Where(b => !IsBillPaid(b) && b.EffectiveDueDate.Date <= payEnd).Sum(b => b.AmountDollars);
        var afterBills = TotalBalance - billsTotal;
        return (TotalBalance, afterBills, afterBills + EstimatedPayAmount);
    }

    public (decimal AfterBills, decimal AfterPay) GetAccountForecast(int accountId)
    {
        var today    = DateTime.Today;
        var payEnd   = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);
        var current  = GetAccountBalance(accountId);
        var billsDue = Bills
            .Where(b => b.AccountId == accountId && !IsBillPaid(b) && b.EffectiveDueDate.Date <= payEnd)
            .Sum(b => b.AmountDollars);
        var afterBills = current - billsDue;
        return (afterBills, afterBills + (accountId == PayAccountId ? EstimatedPayAmount : 0m));
    }

    public bool IsInternalMovement(Transaction t) =>
        TransactionClassification.IsInternalMovementCategory(t.CategoryName) ||
        TransactionClassification.HasLinkedTransferId(t) ||
        TransactionClassification.IsCoverMovementDescription(t.Description) ||
        _matchedInternalMovementIds.Contains(t.Id);

    public bool IsBudgetedBillTransaction(Transaction t) =>
        IsBillCategory(t.CategoryName) ||
        MatchesKnownBudgetedPayment(t) ||
        MatchesBillRecord(t);

    private bool MatchesBillRecord(Transaction t)
    {
        if (t.AmountCents >= 0) return false;
        var amount = Math.Abs(t.AmountCents);
        return Bills.Any(b =>
            Math.Abs(b.AmountCents - amount) <= 1 &&
            (string.Equals(b.AccountName, t.AccountName, StringComparison.OrdinalIgnoreCase) || b.AccountId == t.AccountId) &&
            (TextContainsToken(t.Description, b.Name) || TextContainsToken(t.Description, b.PaymentMatchText)));
    }

    private static bool MatchesKnownBudgetedPayment(Transaction t)
    {
        if (t.AmountCents >= 0) return false;
        var description = t.Description ?? string.Empty;
        return description.Contains("Nissan Financial", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Skyline Car Finance", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Australian College of Commerce", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Swoosh Finance", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TextContainsToken(string text, string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        text.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase);

    // ── Daily tracker ─────────────────────────────────────────────────────────
    public List<DailyScore> GetDailyScores(int days = 35)
    {
        var today = DateTime.Today;
        var txByDate = Transactions
            .Where(t => t.AmountCents < 0 && !IsInternalMovement(t))
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DailyScore>(days);
        for (int i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var dayTxns = txByDate.GetValueOrDefault(date);
            var spending = dayTxns?.Sum(t => Math.Abs(t.AmountDollars)) ?? 0m;
            var unnecessary = dayTxns?.Where(t => t.IsUnnecessary).Sum(t => Math.Abs(t.AmountDollars)) ?? 0m;
            var necessary = spending - unnecessary;
            var score = spending == 0 ? 100 : (int)(necessary / spending * 100);
            var grade = spending == 0 ? "—" : score switch
            {
                100 => "A+", >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 50 => "D", _ => "F"
            };
            var color = spending == 0 ? "" : score switch
            {
                100 => "#34D399", >= 80 => "#6EE7B7", >= 60 => "#FBBF24", >= 40 => "#F97316", _ => "#F87171"
            };
            result.Add(new DailyScore(date, spending, unnecessary, score, grade, color));
        }
        return result;
    }

    public int GetCleanStreak()
    {
        var txByDay = Transactions
            .Where(t => t.AmountCents < 0 && !IsInternalMovement(t))
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var streak = 0;
        for (var i = 1; i <= 60; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            if (!txByDay.TryGetValue(date, out var dayTx) || dayTx.Count == 0) { streak++; continue; }
            if (dayTx.Any(t => t.IsUnnecessary)) break;
            streak++;
        }
        return streak;
    }

    // ── Category classification (matches WPF logic) ───────────────────────────
    private static readonly HashSet<string> _billCats = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rent", "Phone", "Mobile Phone", "Internet", "Car Loan", "Insurance",
        "Car Insurance And Maintenance", "Debt"
    };
    private static readonly HashSet<string> _essentialCats = new(StringComparer.OrdinalIgnoreCase)
    {
        "Groceries", "Fuel", "Medical", "Study"
    };
    public static bool IsBillCategory(string name) => _billCats.Contains(name);
    public static bool IsEssentialCategory(string name) => _essentialCats.Contains(name);

    // ── Write operations (store locally + queue for sync) ─────────────────────
    public async Task AddTransactionAsync(Transaction t)
    {
        // Normalise date to DateTimeKind.Unspecified (date-only, no time, no offset).
        // DateTime.Today in Blazor WASM has Kind=Local, which System.Text.Json serialises
        // with the local offset (e.g. +10:00). The PC then converts it to UTC, shifting
        // the date by the timezone offset. Stripping Kind here prevents that round-trip.
        t.Date = new DateTime(t.Date.Year, t.Date.Month, t.Date.Day);

        // Assign a temp negative ID for phone-created records
        var minId = Transactions.Count > 0 ? Transactions.Min(x => x.Id) : 0;
        t.Id = Math.Min(minId - 1, -1);
        t.AccountName = Accounts.FirstOrDefault(a => a.Id == t.AccountId)?.Name ?? "";
        t.CategoryName = Categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "";
        Transactions.Add(t);
        _pendingNewTransactions.Add(t);
        await db.PutAsync("transactions", t);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task ToggleUnnecessaryAsync(int transactionId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (t is null) return;
        t.IsUnnecessary = !t.IsUnnecessary;
        await db.PutAsync("transactions", t);
        if (transactionId > 0)
        {
            QueueUpdatedTransaction(t);
            await db.SetTransactionOverrideAsync(t);
        }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task UpdateTransactionFullAsync(Transaction updated)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == updated.Id);
        if (t is null) return;
        ApplyTransactionEdit(t, updated);
        await db.PutAsync("transactions", t);
        if (t.Id > 0)
        {
            QueueUpdatedTransaction(t);
            await db.SetTransactionOverrideAsync(t);
        }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task AddBillAsync(Bill b)
    {
        var minId = Bills.Count > 0 ? Bills.Min(x => x.Id) : 0;
        b.Id = Math.Min(minId - 1, -1);
        b.AccountName = Accounts.FirstOrDefault(a => a.Id == b.AccountId)?.Name ?? "";
        Bills.Add(b);
        _pendingNewBills.Add(b);
        await db.PutAsync("bills", b);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task UpdateBillAsync(Bill b)
    {
        var existing = Bills.FirstOrDefault(x => x.Id == b.Id);
        if (existing is null) return;
        existing.Name = b.Name;
        existing.AccountId = b.AccountId;
        existing.AccountName = Accounts.FirstOrDefault(a => a.Id == b.AccountId)?.Name ?? "";
        existing.AmountDollars = b.AmountDollars;
        existing.DueDate = b.DueDate;
        existing.Frequency = b.Frequency;
        existing.IsAutoPay = b.IsAutoPay;
        await db.PutAsync("bills", existing);
        if (b.Id > 0 && !_pendingUpdatedBills.Any(x => x.Id == b.Id))
            _pendingUpdatedBills.Add(existing);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task UpdateTransactionCategoryAsync(int transactionId, int categoryId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (t is null) return;
        t.CategoryId = categoryId;
        t.CategoryName = Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "";
        await db.PutAsync("transactions", t);
        if (transactionId > 0)
        {
            QueueUpdatedTransaction(t);
            await db.SetTransactionOverrideAsync(t);
        }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task DeleteTransactionAsync(int id)
    {
        Transactions.RemoveAll(t => t.Id == id);
        if (id > 0) _pendingDeletedTransactionIds.Add(id);
        await db.DeleteAsync("transactions", id);
        await db.ClearTransactionOverrideAsync(id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task MarkBillPaidAsync(int billId, bool paid)
    {
        var bill = Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null) return;
        bill.IsPaid = paid;
        await db.PutAsync("bills", bill);

        var status = GetOrCreateCurrentStatus(bill);
        status.IsPaid = paid;
        status.PaidOn = paid ? DateTime.Now : null;

        _pendingBillStatuses.Add(status);
        await db.PutAsync("billOccurrenceStatuses", status);
        // Persist the override so it survives sync's clearAll and app restarts
        await db.SetBillOverrideAsync(billId, paid);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    // Push phone edits to the cloud a couple of seconds after they happen,
    // instead of waiting for the 5-minute periodic timer in MainLayout —
    // iOS suspends a backgrounded "Add to Home Screen" PWA almost
    // immediately, so that timer rarely gets a chance to fire. Debounced so
    // a burst of edits results in one sync, not one per edit.
    private void ScheduleSyncSoon()
    {
        _syncDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _syncDebounceCts = cts;
        _ = DebouncedSyncAsync(cts.Token);
    }

    private async Task DebouncedSyncAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        try { await SyncAndReloadAsync(); }
        catch { /* SyncService records LastError for the Settings page */ }
    }

    private void ApplyTransactionEdit(Transaction target, Transaction updated)
    {
        target.Date = new DateTime(updated.Date.Year, updated.Date.Month, updated.Date.Day);
        target.Description = updated.Description;
        target.AmountDollars = updated.AmountDollars;
        target.AccountId = updated.AccountId;
        target.AccountName = Accounts.FirstOrDefault(a => a.Id == updated.AccountId)?.Name ?? "";
        target.CategoryId = updated.CategoryId;
        target.CategoryName = Categories.FirstOrDefault(c => c.Id == updated.CategoryId)?.Name ?? "";
        target.TransferId = updated.TransferId;
        target.UpTransactionId = updated.UpTransactionId;
        target.IsUnnecessary = updated.IsUnnecessary;
    }

    private void QueueUpdatedTransaction(Transaction transaction)
    {
        if (transaction.Id <= 0) return;
        _pendingUpdatedTransactions.RemoveAll(t => t.Id == transaction.Id);
        _pendingUpdatedTransactions.Add(transaction);
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await db.SaveSettingAsync(key, value);
        AppSettings = await db.GetAppSettingsAsync();
        var setting = AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting is not null)
        {
            _pendingUpdatedSettings.RemoveAll(s => s.Key == key);
            _pendingUpdatedSettings.Add(setting);
        }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    // ── Merge phone-side pending changes into the cloud's finance_sync ────────
    // snapshot, so the cloud stays current even if the PC never drains
    // phone_push. Phone-created rows keep their negative temp IDs here; when
    // WPF next runs, ApplyPhonePushAsync assigns them real IDs and WPF's next
    // push naturally supersedes these entries (no duplicates, since
    // _pendingXxx is cleared before this is called).
    private async Task<SyncPayload?> BuildMergedCloudPayloadAsync(PushPayload push)
    {
        var cloud = await sync.FetchCloudPayloadAsync();
        if (cloud is null) return null;

        cloud.Transactions.RemoveAll(t => push.DeletedTransactionIds.Contains(t.Id));
        foreach (var u in push.UpdatedTransactions)
        {
            var existing = cloud.Transactions.FirstOrDefault(t => t.Id == u.Id);
            if (existing is null) continue;
            existing.Description = u.Description;
            existing.AmountCents = u.AmountCents;
            existing.Date = u.Date;
            existing.AccountId = u.AccountId;
            existing.CategoryId = u.CategoryId;
            existing.TransferId = u.TransferId;
            existing.UpTransactionId = u.UpTransactionId;
            existing.IsUnnecessary = u.IsUnnecessary;
        }
        cloud.Transactions.AddRange(push.NewTransactions);

        foreach (var u in push.UpdatedBills)
        {
            var existing = cloud.Bills.FirstOrDefault(b => b.Id == u.Id);
            if (existing is null) continue;
            existing.Name = u.Name;
            existing.AccountId = u.AccountId;
            existing.AmountCents = u.AmountCents;
            existing.DueDate = u.DueDate;
            existing.Frequency = u.Frequency;
            existing.IsAutoPay = u.IsAutoPay;
        }
        cloud.Bills.AddRange(push.NewBills);

        foreach (var s in push.UpdatedBillStatuses)
        {
            var existing = cloud.BillOccurrenceStatuses
                .FirstOrDefault(x => x.BillId == s.BillId && x.DueDate.Date == s.DueDate.Date);
            if (existing is not null)
            {
                existing.IsPaid = s.IsPaid;
                existing.PaidOn = s.PaidOn;
            }
            else
            {
                cloud.BillOccurrenceStatuses.Add(s);
            }

            var bill = cloud.Bills.FirstOrDefault(b => b.Id == s.BillId);
            if (bill is not null) bill.IsPaid = s.IsPaid;
        }

        foreach (var setting in push.UpdatedSettings)
        {
            var existing = cloud.AppSettings.FirstOrDefault(s => s.Key == setting.Key);
            if (existing is null)
            {
                cloud.AppSettings.Add(setting);
            }
            else
            {
                existing.Value = setting.Value;
            }
        }

        cloud.SyncedAt = DateTime.UtcNow;
        return cloud;
    }

    public async Task SyncAndReloadAsync()
    {
        // Re-entrancy guard: ScheduleSyncSoon (after every edit) and the
        // 5-minute periodic timer can otherwise overlap and race on
        // _pendingXxx / IndexedDB.
        if (_syncInProgress) return;
        _syncInProgress = true;
        try
        {
            // Push any phone-side changes — Wi-Fi first, Supabase as fallback
            PushPayload? sentPush = null;

            if (HasPendingChanges)
            {
                var push = new PushPayload
                {
                    NewTransactions = new List<Transaction>(_pendingNewTransactions),
                    UpdatedTransactions = new List<Transaction>(_pendingUpdatedTransactions),
                    DeletedTransactionIds = new List<int>(_pendingDeletedTransactionIds),
                    UpdatedBillStatuses = new List<BillOccurrenceStatus>(_pendingBillStatuses),
                    NewBills = new List<Bill>(_pendingNewBills),
                    UpdatedBills = new List<Bill>(_pendingUpdatedBills),
                    UpdatedSettings = new List<AppSetting>(_pendingUpdatedSettings)
                };

                bool pushedToPc = false;
                if (sync.HasLocalSync)
                    pushedToPc = await sync.PushToPcAsync(push);

                bool pushedToCloud = false;
                bool pushedToCanonicalStore = false;
                if (sync.HasCloudSync)
                {
                    pushedToCloud = await sync.PushToSupabaseAsync(push);

                    // Also merge straight into finance_sync (the canonical cloud
                    // snapshot) so the cloud is current even if the PC never comes
                    // back online to drain phone_push. Best-effort: phone_push
                    // above is what WPF reconciles from, so a failure here can't
                    // regress anything.
                    if (pushedToCloud || pushedToPc)
                    {
                        var merged = await BuildMergedCloudPayloadAsync(push);
                        if (merged is not null)
                            pushedToCanonicalStore = await sync.PushFullSyncAsync(merged);
                    }
                }
                else
                {
                    pushedToCanonicalStore = pushedToPc;
                }

                var pushed = pushedToCanonicalStore;
                if (pushed)
                {
                    sentPush = push;
                    foreach (var t in push.UpdatedTransactions)
                        await db.ClearTransactionOverrideAsync(t.Id);
                    foreach (var s in push.UpdatedBillStatuses)
                        await db.ClearBillOverrideAsync(s.BillId);
                    // Remove only what was actually sent (by count, not Clear) —
                    // an edit made while this push was in flight appends to
                    // these lists and must survive for the next sync.
                    _pendingNewTransactions.RemoveRange(0, push.NewTransactions.Count);
                    _pendingUpdatedTransactions.RemoveRange(0, push.UpdatedTransactions.Count);
                    _pendingDeletedTransactionIds.RemoveRange(0, push.DeletedTransactionIds.Count);
                    _pendingBillStatuses.RemoveRange(0, push.UpdatedBillStatuses.Count);
                    _pendingNewBills.RemoveRange(0, push.NewBills.Count);
                    _pendingUpdatedBills.RemoveRange(0, push.UpdatedBills.Count);
                    _pendingUpdatedSettings.RemoveRange(0, push.UpdatedSettings.Count);
                }
            }

            // Pull data — cloud first, then local Wi-Fi
            var ok = await sync.AutoSyncAsync();
            if (ok)
            {
                await LoadAsync();
                // Sync wipes IndexedDB and replaces with server data; reapply any
                // phone-side changes that weren't pushed so they aren't lost.
                if (sentPush is not null)
                    await ReapplyPushChangesAsync(sentPush);
                await ReapplyPendingChangesAsync();
            }
            else OnChange?.Invoke();
        }
        finally
        {
            _syncInProgress = false;
        }
    }

    public async Task MarkPendingChangesSyncedAsync()
    {
        _pendingNewTransactions.Clear();
        _pendingUpdatedTransactions.Clear();
        _pendingDeletedTransactionIds.Clear();
        _pendingBillStatuses.Clear();
        _pendingNewBills.Clear();
        _pendingUpdatedBills.Clear();
        _pendingUpdatedSettings.Clear();
        await db.ClearTransactionOverridesAsync();
        await db.ClearBillOverridesAsync();
        OnChange?.Invoke();
    }

    private async Task ReapplyPendingChangesAsync()
    {
        var push = new PushPayload
        {
            NewTransactions = new List<Transaction>(_pendingNewTransactions),
            UpdatedTransactions = new List<Transaction>(_pendingUpdatedTransactions),
            DeletedTransactionIds = new List<int>(_pendingDeletedTransactionIds),
            UpdatedBillStatuses = new List<BillOccurrenceStatus>(_pendingBillStatuses),
            NewBills = new List<Bill>(_pendingNewBills),
            UpdatedBills = new List<Bill>(_pendingUpdatedBills),
            UpdatedSettings = new List<AppSetting>(_pendingUpdatedSettings)
        };

        await ReapplyPushChangesAsync(push);
    }

    private async Task ReapplyPushChangesAsync(PushPayload push)
    {
        bool changed = false;

        // Re-add phone-created transactions the server doesn't know about yet
        foreach (var t in push.NewTransactions)
        {
            if (!Transactions.Any(x => x.Id == t.Id))
            {
                Transactions.Add(t);
                await db.PutAsync("transactions", t);
                changed = true;
            }
        }

        // Re-apply transaction edits made on phone
        foreach (var pt in push.UpdatedTransactions)
        {
            var t = Transactions.FirstOrDefault(x => x.Id == pt.Id);
            if (t is null) continue;
            t.Date = pt.Date;
            t.Description = pt.Description;
            t.AmountCents = pt.AmountCents;
            t.AccountId = pt.AccountId;
            t.AccountName = pt.AccountName;
            t.CategoryId = pt.CategoryId;
            t.CategoryName = pt.CategoryName;
            t.TransferId = pt.TransferId;
            t.UpTransactionId = pt.UpTransactionId;
            t.IsUnnecessary = pt.IsUnnecessary;
            await db.PutAsync("transactions", t);
            changed = true;
        }

        // Re-remove phone-deleted transactions
        foreach (var id in push.DeletedTransactionIds)
        {
            if (Transactions.RemoveAll(x => x.Id == id) > 0)
            {
                await db.DeleteAsync("transactions", id);
                changed = true;
            }
        }

        // Re-apply bill paid/unpaid changes
        foreach (var ps in push.UpdatedBillStatuses)
        {
            var bill = Bills.FirstOrDefault(b => b.Id == ps.BillId);
            if (bill is null) continue;
            bill.IsPaid = ps.IsPaid;
            await db.PutAsync("bills", bill);

            var existing = GetOrCreateCurrentStatus(bill);
            existing.IsPaid = ps.IsPaid;
            existing.PaidOn = ps.PaidOn;
            await db.PutAsync("billOccurrenceStatuses", existing);
            changed = true;
        }

        // Re-add phone-created bills the server doesn't know about yet
        foreach (var b in push.NewBills)
        {
            if (!Bills.Any(x => x.Id == b.Id))
            {
                Bills.Add(b);
                await db.PutAsync("bills", b);
                changed = true;
            }
        }

        // Re-apply bill edits made on phone
        foreach (var pb in push.UpdatedBills)
        {
            var bill = Bills.FirstOrDefault(x => x.Id == pb.Id);
            if (bill is null) continue;
            bill.Name = pb.Name;
            bill.AmountDollars = pb.AmountDollars;
            bill.DueDate = pb.DueDate;
            bill.Frequency = pb.Frequency;
            bill.IsAutoPay = pb.IsAutoPay;
            bill.AccountId = pb.AccountId;
            bill.AccountName = pb.AccountName;
            await db.PutAsync("bills", bill);
            changed = true;
        }

        // Re-apply phone-side settings such as category limits/payday/summary period.
        foreach (var ps in push.UpdatedSettings)
        {
            await db.SaveSettingAsync(ps.Key, ps.Value);
            var existing = AppSettings.FirstOrDefault(s => s.Key == ps.Key);
            if (existing is null)
            {
                AppSettings.Add(ps);
            }
            else
            {
                existing.Value = ps.Value;
            }
            changed = true;
        }

        if (changed)
        {
            Compute();
            OnChange?.Invoke();
        }
    }
}
