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

    public bool HasPendingChanges =>
        _pendingNewTransactions.Count > 0 ||
        _pendingUpdatedTransactions.Count > 0 ||
        _pendingDeletedTransactionIds.Count > 0 ||
        _pendingBillStatuses.Count > 0 ||
        _pendingNewBills.Count > 0 ||
        _pendingUpdatedBills.Count > 0;

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

        // Apply any phone-side bill paid/unpaid overrides that survived the last sync
        await ApplyPersistedBillOverridesAsync();

        await sync.InitAsync();
        Compute();
        IsLoaded = true;
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

            var status = BillStatuses
                .Where(s => s.BillId == ov.Id)
                .OrderByDescending(s => s.DueDate)
                .FirstOrDefault();
            if (status is not null)
            {
                status.IsPaid = ov.IsPaid;
                status.PaidOn = ov.IsPaid ? status.PaidOn : null;
            }
        }
    }

    private void Compute()
    {
        ComputeSettings();
        DenormaliseTransactions();
        DenormaliseBills();
        ComputeBalances();
        ComputeBudget();
        ComputeSummaries();
        DetectPayAccount();
        ComputeBillsDue();
        RecentTransactions = Transactions
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Take(200)
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

    private void DenormaliseBills()
    {
        var accountMap = Accounts.ToDictionary(a => a.Id, a => a.Name);
        foreach (var b in Bills)
            b.AccountName = accountMap.GetValueOrDefault(b.AccountId, "");
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
        var today = DateTime.Today;
        var payEnd = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);

        var statusMap = BillStatuses
            .GroupBy(s => s.BillId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.DueDate).FirstOrDefault());

        BillsDueBeforePayday = Bills
            .Where(b =>
            {
                var status = statusMap.GetValueOrDefault(b.Id);
                var isPaid = status?.IsPaid == true || b.IsPaid;
                return !isPaid && b.DueDate.Date <= payEnd;
            })
            .OrderBy(b => b.DueDate)
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
        if (bill.IsPaid) return true;
        var status = BillStatuses
            .Where(s => s.BillId == bill.Id)
            .OrderByDescending(s => s.DueDate)
            .FirstOrDefault();
        return status?.IsPaid == true;
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

    // ── Spending stats ─────────────────────────────────────────────────────────
    // Total spending (all categories) — used for dashboard/trend
    public decimal GetPeriodSpending()
    {
        var (from, to) = GetCurrentPeriod();
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0 && !IsTransfer(t))
            .Sum(t => Math.Abs(t.AmountDollars));
    }

    // Discretionary spending = total minus bill categories (matches WPF budget comparison)
    public decimal GetDiscretionarySpending()
    {
        var (from, to) = GetCurrentPeriod();
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsTransfer(t) && !IsBillCategory(t.CategoryName))
            .Sum(t => Math.Abs(t.AmountDollars));
    }

    // Weekly discretionary budget = essentials + unplanned
    public decimal DiscretionaryBudget => BudgetEssentials + BudgetUnplanned;

    public decimal GetTodaySpending() =>
        Transactions
            .Where(t => t.Date.Date == DateTime.Today && t.AmountCents < 0 && !IsTransfer(t))
            .Sum(t => Math.Abs(t.AmountDollars));

    public List<(string Category, decimal Amount)> GetTopCategories(int n = 5, bool excludeBills = false)
    {
        var (from, to) = GetCurrentPeriod();
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsTransfer(t) && (!excludeBills || !IsBillCategory(t.CategoryName)))
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
                         b.DueDate.Date >= DateTime.Today &&
                         b.DueDate.Date <= DateTime.Today.AddDays(days))
             .OrderBy(b => b.DueDate)
             .ToList();

    public List<(string Month, decimal Income, decimal Spending)> GetMonthlyTrend(int months = 6)
    {
        var result = new List<(string, decimal, decimal)>();
        for (int i = months - 1; i >= 0; i--)
        {
            var d = DateTime.Today.AddMonths(-i);
            var from = new DateTime(d.Year, d.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var txs = Transactions.Where(t => t.Date >= from && t.Date <= to && !IsTransfer(t)).ToList();
            var inc = txs.Where(t => t.AmountCents > 0).Sum(t => t.AmountDollars);
            var spend = txs.Where(t => t.AmountCents < 0).Sum(t => Math.Abs(t.AmountDollars));
            result.Add((d.ToString("MMM"), inc, spend));
        }
        return result;
    }

    private void DetectPayAccount()
    {
        var payTxns = Transactions
            .Where(t => t.AmountCents >= 20000 && !IsTransfer(t))
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
        var today = DateTime.Today;
        var payEnd = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);
        var billsTotal = Bills.Where(b => !IsBillPaid(b) && b.DueDate.Date <= payEnd).Sum(b => b.AmountDollars);
        var afterBills = TotalBalance - billsTotal;
        return (TotalBalance, afterBills, afterBills + EstimatedPayAmount);
    }

    public (decimal AfterBills, decimal AfterPay) GetAccountForecast(int accountId)
    {
        var today = DateTime.Today;
        var payEnd = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);
        var current = GetAccountBalance(accountId);
        var billsDue = Bills
            .Where(b => b.AccountId == accountId && !IsBillPaid(b) && b.DueDate.Date <= payEnd)
            .Sum(b => b.AmountDollars);
        var afterBills = current - billsDue;
        return (afterBills, afterBills + (accountId == PayAccountId ? EstimatedPayAmount : 0m));
    }

    private static bool IsTransfer(Transaction t) =>
        t.CategoryName is "Transfer" or "Opening Balance" or "Balance Adjustment" ||
        (t.TransferId is { } tid && tid != Guid.Empty);

    // ── Daily tracker ─────────────────────────────────────────────────────────
    public List<DailyScore> GetDailyScores(int days = 35)
    {
        var today = DateTime.Today;
        var txByDate = Transactions
            .Where(t => t.AmountCents < 0 && !IsTransfer(t))
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
            .Where(t => t.AmountCents < 0 && !IsTransfer(t))
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
    }

    public async Task ToggleUnnecessaryAsync(int transactionId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (t is null) return;
        t.IsUnnecessary = !t.IsUnnecessary;
        await db.PutAsync("transactions", t);
        if (transactionId > 0 && !_pendingUpdatedTransactions.Any(x => x.Id == transactionId))
            _pendingUpdatedTransactions.Add(t);
        Compute();
        OnChange?.Invoke();
    }

    public async Task UpdateTransactionFullAsync(Transaction updated)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == updated.Id);
        if (t is null) return;
        t.Date = updated.Date;
        t.Description = updated.Description;
        t.AmountDollars = updated.AmountDollars;
        t.AccountId = updated.AccountId;
        t.AccountName = Accounts.FirstOrDefault(a => a.Id == updated.AccountId)?.Name ?? "";
        t.CategoryId = updated.CategoryId;
        t.CategoryName = Categories.FirstOrDefault(c => c.Id == updated.CategoryId)?.Name ?? "";
        t.IsUnnecessary = updated.IsUnnecessary;
        await db.PutAsync("transactions", t);
        if (t.Id > 0 && !_pendingUpdatedTransactions.Any(x => x.Id == t.Id))
            _pendingUpdatedTransactions.Add(t);
        Compute();
        OnChange?.Invoke();
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
    }

    public async Task UpdateTransactionCategoryAsync(int transactionId, int categoryId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (t is null) return;
        t.CategoryId = categoryId;
        t.CategoryName = Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "";
        await db.PutAsync("transactions", t);
        if (transactionId > 0 && !_pendingUpdatedTransactions.Any(x => x.Id == transactionId))
            _pendingUpdatedTransactions.Add(t);
        Compute();
        OnChange?.Invoke();
    }

    public async Task DeleteTransactionAsync(int id)
    {
        Transactions.RemoveAll(t => t.Id == id);
        if (id > 0) _pendingDeletedTransactionIds.Add(id);
        await db.DeleteAsync("transactions", id);
        Compute();
        OnChange?.Invoke();
    }

    public async Task MarkBillPaidAsync(int billId, bool paid)
    {
        var bill = Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null) return;
        bill.IsPaid = paid;
        await db.PutAsync("bills", bill);

        var status = BillStatuses
            .Where(s => s.BillId == billId)
            .OrderByDescending(s => s.DueDate)
            .FirstOrDefault() ??
            new BillOccurrenceStatus { Id = -(BillStatuses.Count + 1), BillId = billId, DueDate = bill.DueDate };
        status.IsPaid = paid;
        status.PaidOn = paid ? DateTime.Now : null;

        if (!BillStatuses.Contains(status)) BillStatuses.Add(status);
        _pendingBillStatuses.Add(status);
        await db.PutAsync("billOccurrenceStatuses", status);
        // Persist the override so it survives sync's clearAll and app restarts
        await db.SetBillOverrideAsync(billId, paid);
        Compute();
        OnChange?.Invoke();
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await db.SaveSettingAsync(key, value);
        AppSettings = await db.GetAppSettingsAsync();
        Compute();
        OnChange?.Invoke();
    }

    public async Task SyncAndReloadAsync()
    {
        // Push any phone-side changes — Wi-Fi first, Supabase as fallback
        if (HasPendingChanges)
        {
            var push = new PushPayload
            {
                NewTransactions = new List<Transaction>(_pendingNewTransactions),
                UpdatedTransactions = new List<Transaction>(_pendingUpdatedTransactions),
                DeletedTransactionIds = new List<int>(_pendingDeletedTransactionIds),
                UpdatedBillStatuses = new List<BillOccurrenceStatus>(_pendingBillStatuses),
                NewBills = new List<Bill>(_pendingNewBills),
                UpdatedBills = new List<Bill>(_pendingUpdatedBills)
            };

            bool pushed = false;
            if (sync.HasLocalSync)
                pushed = await sync.PushToPcAsync(push);

            if (!pushed && sync.HasCloudSync)
                pushed = await sync.PushToSupabaseAsync(push);

            if (pushed)
            {
                foreach (var s in _pendingBillStatuses)
                    await db.ClearBillOverrideAsync(s.BillId);
                _pendingNewTransactions.Clear();
                _pendingUpdatedTransactions.Clear();
                _pendingDeletedTransactionIds.Clear();
                _pendingBillStatuses.Clear();
                _pendingNewBills.Clear();
                _pendingUpdatedBills.Clear();
            }
        }

        // Pull data — cloud first, then local Wi-Fi
        var ok = await sync.AutoSyncAsync();
        if (ok)
        {
            await LoadAsync();
            // Sync wipes IndexedDB and replaces with server data; reapply any
            // phone-side changes that weren't pushed so they aren't lost.
            await ReapplyPendingChangesAsync();
        }
        else OnChange?.Invoke();
    }

    private async Task ReapplyPendingChangesAsync()
    {
        bool changed = false;

        // Re-add phone-created transactions the server doesn't know about yet
        foreach (var t in _pendingNewTransactions)
        {
            if (!Transactions.Any(x => x.Id == t.Id))
            {
                Transactions.Add(t);
                await db.PutAsync("transactions", t);
                changed = true;
            }
        }

        // Re-apply category / unnecessary changes
        foreach (var pt in _pendingUpdatedTransactions)
        {
            var t = Transactions.FirstOrDefault(x => x.Id == pt.Id);
            if (t is null) continue;
            t.CategoryId = pt.CategoryId;
            t.CategoryName = pt.CategoryName;
            t.IsUnnecessary = pt.IsUnnecessary;
            await db.PutAsync("transactions", t);
            changed = true;
        }

        // Re-remove phone-deleted transactions
        foreach (var id in _pendingDeletedTransactionIds)
        {
            if (Transactions.RemoveAll(x => x.Id == id) > 0)
            {
                await db.DeleteAsync("transactions", id);
                changed = true;
            }
        }

        // Re-apply bill paid/unpaid changes
        foreach (var ps in _pendingBillStatuses)
        {
            var bill = Bills.FirstOrDefault(b => b.Id == ps.BillId);
            if (bill is null) continue;
            bill.IsPaid = ps.IsPaid;
            await db.PutAsync("bills", bill);

            var existing = BillStatuses
                .Where(s => s.BillId == ps.BillId)
                .OrderByDescending(s => s.DueDate)
                .FirstOrDefault();
            if (existing is not null)
            {
                existing.IsPaid = ps.IsPaid;
                existing.PaidOn = ps.PaidOn;
                await db.PutAsync("billOccurrenceStatuses", existing);
            }
            else
            {
                BillStatuses.Add(ps);
                await db.PutAsync("billOccurrenceStatuses", ps);
            }
            changed = true;
        }

        // Re-add phone-created bills the server doesn't know about yet
        foreach (var b in _pendingNewBills)
        {
            if (!Bills.Any(x => x.Id == b.Id))
            {
                Bills.Add(b);
                await db.PutAsync("bills", b);
                changed = true;
            }
        }

        // Re-apply bill edits made on phone
        foreach (var pb in _pendingUpdatedBills)
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

        if (changed)
        {
            Compute();
            OnChange?.Invoke();
        }
    }
}
