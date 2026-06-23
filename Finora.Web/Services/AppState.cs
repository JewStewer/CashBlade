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
    public List<DebtPayment> DebtPayments { get; private set; } = new();
    public List<SavingsGoal> SavingsGoals { get; private set; } = new();
    public List<WeeklyBudget> WeeklyBudgets { get; private set; } = new();
    public List<AppSetting> AppSettings { get; private set; } = new();
    public List<Trip> Trips { get; private set; } = new();
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
    public decimal PlannedSavingsTransfers { get; private set; }
    public decimal BudgetWeeklyTransfers => BudgetBills + BudgetSavings;
    public decimal BudgetUnplanned { get; private set; }
    public decimal BudgetLeftover => WeeklyIncome - BudgetBills - BudgetEssentials - BudgetSavings - BudgetUnplanned;
    public decimal SafeToSpendAmount => Math.Max(BudgetLeftover, 0);

    // ── Settings ──────────────────────────────────────────────────────────────
    public DateTime NextPayDate { get; private set; } = DateTime.Today;
    public string SummaryPeriod { get; private set; } = "Monthly";

    // ── Affordability savings goal (shared setting keys with WPF) ─────────────
    public decimal AffordabilityGoalAmount { get; private set; }
    public int AffordabilityGoalWeeks { get; private set; } = 4;
    public string AffordabilityGoalAccountName { get; private set; } = string.Empty;
    public Account? AffordabilityGoalAccount =>
        string.IsNullOrWhiteSpace(AffordabilityGoalAccountName)
            ? null
            : Accounts.FirstOrDefault(a => string.Equals(a.Name, AffordabilityGoalAccountName, StringComparison.OrdinalIgnoreCase));

    // ── Affordability calculator mode + installment plan (Afterpay-style) ─────
    public string AffordabilityMode { get; private set; } = "OneOff";
    public decimal AffordabilityInstallmentAmount { get; private set; }
    public int AffordabilityInstallmentCount { get; private set; } = 4;
    public int AffordabilityInstallmentFrequencyWeeks { get; private set; } = 2;

    // ── App lock (PIN) ──────────────────────────────────────────────────────────
    private const string AppLockPinHashKey = "AppLockPinHash";
    private const string AppLockSalt = "Finora-AppLock-v1";
    private bool _lockStateInitialized;

    public bool AppLockEnabled { get; private set; }
    // In-memory only: reset on the first Compute() after a fresh app load, then
    // left alone so background syncs/setting changes don't re-lock mid-session.
    public bool IsUnlocked { get; private set; } = true;

    // ── Pending phone-side changes (synced on next push) ──────────────────────
    private readonly List<Transaction> _pendingNewTransactions = new();
    private readonly List<Transaction> _pendingUpdatedTransactions = new();
    private readonly List<int> _pendingDeletedTransactionIds = new();
    private readonly List<TransactionDelete> _pendingDeletedTransactions = new();
    private readonly List<BillOccurrenceStatus> _pendingBillStatuses = new();
    private readonly List<Bill> _pendingNewBills = new();
    private readonly List<Bill> _pendingUpdatedBills = new();
    private readonly List<AppSetting> _pendingUpdatedSettings = new();
    private readonly List<int> _pendingDeletedBillIds = new();
    private readonly List<BillDelete> _pendingDeletedBills = new();
    private readonly List<Debt> _pendingNewDebts = new();
    private readonly List<Debt> _pendingUpdatedDebts = new();
    private readonly List<int> _pendingDeletedDebtIds = new();
    private readonly List<DebtPayment> _pendingNewDebtPayments = new();
    private readonly List<int> _pendingDeletedDebtPaymentIds = new();
    private readonly List<Account> _pendingUpdatedAccounts = new();
    private readonly List<SavingsGoal> _pendingNewSavingsGoals = new();
    private readonly List<SavingsGoal> _pendingUpdatedSavingsGoals = new();
    private readonly List<int> _pendingDeletedSavingsGoalIds = new();
    private readonly List<Trip> _pendingNewTrips = new();
    private readonly List<Trip> _pendingUpdatedTrips = new();
    private readonly List<int> _pendingDeletedTripIds = new();

    // Debounce + re-entrancy guard for ScheduleSyncSoon/SyncAndReloadAsync —
    // iOS suspends a backgrounded PWA almost immediately, so the 5-minute
    // periodic timer rarely survives long enough to push an edit on its own.
    private CancellationTokenSource? _syncDebounceCts;
    private bool _syncInProgress;
    private DateTime _syncStartedAt;

    // Called by MainLayout on every app-visible event so a sync interrupted by
    // iOS suspension doesn't permanently block the guard.
    public void ForceResetSyncGuard() => _syncInProgress = false;

    public string LastSyncChangeSummary { get; private set; } = "No sync changes summarized yet.";

    public bool HasPendingChanges =>
        _pendingNewTransactions.Count > 0 ||
        _pendingUpdatedTransactions.Count > 0 ||
        _pendingDeletedTransactionIds.Count > 0 ||
        _pendingDeletedTransactions.Count > 0 ||
        _pendingBillStatuses.Count > 0 ||
        _pendingNewBills.Count > 0 ||
        _pendingUpdatedBills.Count > 0 ||
        _pendingUpdatedSettings.Count > 0 ||
        _pendingDeletedBillIds.Count > 0 ||
        _pendingDeletedBills.Count > 0 ||
        _pendingNewDebts.Count > 0 ||
        _pendingUpdatedDebts.Count > 0 ||
        _pendingDeletedDebtIds.Count > 0 ||
        _pendingNewDebtPayments.Count > 0 ||
        _pendingDeletedDebtPaymentIds.Count > 0 ||
        _pendingUpdatedAccounts.Count > 0 ||
        _pendingNewSavingsGoals.Count > 0 ||
        _pendingUpdatedSavingsGoals.Count > 0 ||
        _pendingDeletedSavingsGoalIds.Count > 0 ||
        _pendingNewTrips.Count > 0 ||
        _pendingUpdatedTrips.Count > 0 ||
        _pendingDeletedTripIds.Count > 0;

    public async Task<(int Edits, int Deletes)> GetPersistedTransactionIntentCountsAsync()
    {
        var edits = await db.GetPendingTransactionOverridesAsync();
        var deletes = await db.GetPendingTransactionDeletesAsync();
        return (edits.Count, deletes.Count);
    }

    public event Action? OnChange;

    // ── Account balances computed from transactions ───────────────────────────
    public Dictionary<int, decimal> AccountBalances { get; private set; } = new();

    // ── Bills for current period ──────────────────────────────────────────────
    public List<Bill> BillsDueBeforePayday { get; private set; } = new();
    public decimal TotalBillsDue { get; private set; }

    // ── Transactions for display (most recent 100) ────────────────────────────
    public List<Transaction> RecentTransactions { get; private set; } = new();

    public bool IsLoaded { get; private set; }
    public bool HasAnyFinanceData =>
        Accounts.Count > 0 ||
        Transactions.Count > 0 ||
        Bills.Count > 0 ||
        Debts.Count > 0 ||
        SavingsGoals.Count > 0 ||
        WeeklyBudgets.Count > 0 ||
        Trips.Count > 0;

    public async Task LoadAsync()
    {
        await LoadStoresAsync();
        await sync.InitAsync();

        if (!HasAnyFinanceData && (sync.HasCloudSync || sync.HasLocalSync))
        {
            var restored = await sync.AutoSyncAsync();
            if (restored)
            {
                await LoadStoresAsync();
            }
        }

        Compute();
        IsLoaded = true;
        OnChange?.Invoke();
    }

    private async Task LoadStoresAsync()
    {
        Accounts = await db.GetAccountsAsync();
        Categories = await db.GetCategoriesAsync();
        Transactions = await db.GetTransactionsAsync();
        Bills = await db.GetBillsAsync();
        BillStatuses = await db.GetBillStatusesAsync();
        Debts = await db.GetDebtsAsync();
        DebtPayments = await db.GetDebtPaymentsAsync();
        SavingsGoals = await db.GetSavingsGoalsAsync();
        WeeklyBudgets = await db.GetWeeklyBudgetsAsync();
        AppSettings = await db.GetAppSettingsAsync();
        Trips = await db.GetTripsAsync();
        LentTransactions = await db.GetLentTransactionsAsync();
        NormaliseLentRepayments();
        _unrepaidLentIds = LentTransactions.Where(IsLentOutstanding).Select(l => l.Id).ToHashSet();

        // Apply any phone-side overrides that survived a cloud replace or app restart.
        await ApplyPersistedTransactionOverridesAsync();
        await ApplyPersistedTransactionDeletesAsync();
        await ApplyPersistedBillDeletesAsync();
        await ApplyPersistedDebtDeletesAsync();
        await ApplyPersistedSavingsGoalDeletesAsync();
        await ApplyPersistedTripDeletesAsync();
        await ApplyPersistedBillOverridesAsync();
        await ApplyPersistedSettingOverridesAsync();
    }

    // ── Lent money tracking ──────────────────────────────────────────────────
    // Internal cover/envelope transfers can never be "lent out" — guard against
    // a stale LentTransaction record (e.g. left over from a transaction Id that
    // got reused by a later sync) silently re-attaching to one of these.
    public bool IsLent(int txnId) =>
        LentTransactions.Any(l => l.Id == txnId) && !IsInternalMovementById(txnId);
    public bool IsUnrepaid(int txnId) =>
        _unrepaidLentIds.Contains(txnId) && !IsInternalMovementById(txnId);

    private bool IsInternalMovementById(int txnId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == txnId);
        return t is not null && IsInternalMovement(t);
    }
    public decimal GetLentRepaidDollars(int txnId) =>
        LentTransactions.FirstOrDefault(l => l.Id == txnId)?.RepaidDollars ?? 0m;

    public decimal GetLentOutstandingDollars(Transaction transaction)
    {
        var lent = LentTransactions.FirstOrDefault(l => l.Id == transaction.Id);
        if (lent is null) return 0m;
        return Math.Max(Math.Abs(transaction.AmountDollars) - lent.RepaidDollars, 0m);
    }

    public async Task MarkLentAsync(int txnId, string note)
    {
        LentTransactions.RemoveAll(l => l.Id == txnId);
        var lent = new LentTransaction { Id = txnId, Note = note, Repaid = false, RepaidCents = 0, MarkedAt = DateTime.Now };
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
        var transaction = Transactions.FirstOrDefault(t => t.Id == txnId);
        if (transaction is not null)
        {
            lent.RepaidCents = Math.Abs(transaction.AmountCents);
        }
        lent.Repaid = true;
        _unrepaidLentIds.Remove(txnId);
        await db.SetLentTransactionAsync(lent);
        OnChange?.Invoke();
    }

    public async Task RecordLentRepaymentAsync(int txnId, decimal amountDollars)
    {
        var lent = LentTransactions.FirstOrDefault(l => l.Id == txnId);
        var transaction = Transactions.FirstOrDefault(t => t.Id == txnId);
        if (lent is null || transaction is null || amountDollars <= 0) return;

        var totalCents = Math.Abs(transaction.AmountCents);
        var paidCents = Math.Min(totalCents, lent.RepaidCents + (int)Math.Round(amountDollars * 100m));
        lent.RepaidCents = paidCents;
        lent.Repaid = paidCents >= totalCents;
        if (lent.Repaid) _unrepaidLentIds.Remove(txnId);
        else _unrepaidLentIds.Add(txnId);

        await db.SetLentTransactionAsync(lent);
        OnChange?.Invoke();
    }

    private void NormaliseLentRepayments()
    {
        foreach (var lent in LentTransactions)
        {
            var transaction = Transactions.FirstOrDefault(t => t.Id == lent.Id);
            if (transaction is null) continue;
            if (lent.Repaid && lent.RepaidCents <= 0)
            {
                lent.RepaidCents = Math.Abs(transaction.AmountCents);
            }
            if (lent.RepaidCents >= Math.Abs(transaction.AmountCents))
            {
                lent.Repaid = true;
            }
        }
    }

    private bool IsLentOutstanding(LentTransaction lent)
    {
        var transaction = Transactions.FirstOrDefault(t => t.Id == lent.Id);
        if (transaction is null) return !lent.Repaid;
        return lent.RepaidCents < Math.Abs(transaction.AmountCents);
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

            // Ensure this override reaches the cloud even if the app was
            // suspended before the debounced sync could fire after the original
            // MarkBillPaidAsync call.
            _pendingBillStatuses.RemoveAll(s => s.BillId == status.BillId && s.DueDate.Date == status.DueDate.Date);
            _pendingBillStatuses.Add(status);
        }
    }

    private async Task ApplyPersistedTransactionOverridesAsync()
    {
        var overrides = await db.GetPendingTransactionOverridesAsync();
        foreach (var ov in overrides)
        {
            var updated = ov.Transaction;
            var transaction = FindTransactionForOverride(updated);
            if (transaction is null) continue;

            ApplyTransactionEdit(transaction, updated);
            await db.PutAsync("transactions", transaction);
            QueueUpdatedTransaction(transaction);
        }
    }

    private async Task ApplyPersistedTransactionDeletesAsync()
    {
        var deletes = await db.GetPendingTransactionDeletesAsync();
        foreach (var ov in deletes)
        {
            var transaction = FindLocalTransaction(ov.Deleted);
            if (transaction is null) continue;

            Transactions.Remove(transaction);
            await db.DeleteAsync("transactions", transaction.Id);
            _pendingDeletedTransactionIds.RemoveAll(id => id == transaction.Id);
            if (transaction.Id > 0) _pendingDeletedTransactionIds.Add(transaction.Id);
            _pendingDeletedTransactions.RemoveAll(d => SameTransactionDelete(d, ov.Deleted));
            _pendingDeletedTransactions.Add(ov.Deleted);
        }
    }

    private async Task ApplyPersistedSettingOverridesAsync()
    {
        var overrides = await db.GetPendingSettingOverridesAsync();
        foreach (var ov in overrides)
        {
            var setting = ov.Setting;
            await db.SaveSettingAsync(setting.Key, setting.Value);
            var existing = AppSettings.FirstOrDefault(s => s.Key == setting.Key);
            if (existing is null) AppSettings.Add(setting);
            else existing.Value = setting.Value;

            // Re-queue for push — a setting changed locally but never confirmed
            // pushed (e.g. iOS killed the app mid-sync) must survive an app
            // restart and a stale pull, same as transaction overrides.
            _pendingUpdatedSettings.RemoveAll(s => s.Key == setting.Key);
            _pendingUpdatedSettings.Add(setting);
        }
    }

    private async Task ApplyPersistedBillDeletesAsync()
    {
        var deletes = await db.GetPendingBillDeletesAsync();
        foreach (var deleted in deletes)
        {
            var deleteIntent = deleted.ToBillDelete();
            var removedIds = Bills
                .Where(b => SameBillDelete(b, deleteIntent))
                .Select(b => b.Id)
                .ToHashSet();
            if (removedIds.Count == 0) continue;

            Bills.RemoveAll(b => removedIds.Contains(b.Id));
            BillStatuses.RemoveAll(s => removedIds.Contains(s.BillId));
            _pendingNewBills.RemoveAll(b => removedIds.Contains(b.Id));
            _pendingUpdatedBills.RemoveAll(b => removedIds.Contains(b.Id));
            _pendingBillStatuses.RemoveAll(s => removedIds.Contains(s.BillId));
            foreach (var id in removedIds.Where(id => id > 0))
            {
                _pendingDeletedBillIds.RemoveAll(x => x == id);
                _pendingDeletedBillIds.Add(id);
                await db.DeleteAsync("bills", id);
                await db.ClearBillOverrideAsync(id);
            }
            _pendingDeletedBills.RemoveAll(d => SameBillDelete(d, deleteIntent));
            _pendingDeletedBills.Add(deleteIntent);
        }
    }

    private async Task ApplyPersistedDebtDeletesAsync()
    {
        var deletes = await db.GetPendingDebtDeletesAsync();
        foreach (var deleted in deletes)
        {
            var removedIds = Debts
                .Where(d => SameDebtDelete(d, deleted))
                .Select(d => d.Id)
                .ToHashSet();
            if (removedIds.Count == 0) continue;

            Debts.RemoveAll(d => removedIds.Contains(d.Id));
            DebtPayments.RemoveAll(p => removedIds.Contains(p.DebtId));
            foreach (var bill in Bills.Where(b => b.DebtId.HasValue && removedIds.Contains(b.DebtId.Value)))
            {
                bill.DebtId = null;
            }
            foreach (var id in removedIds.Where(id => id > 0))
            {
                _pendingDeletedDebtIds.RemoveAll(x => x == id);
                _pendingDeletedDebtIds.Add(id);
            }
        }
    }

    private async Task ApplyPersistedSavingsGoalDeletesAsync()
    {
        var deletes = await db.GetPendingSavingsGoalDeletesAsync();
        foreach (var deleted in deletes)
        {
            var removedIds = SavingsGoals
                .Where(g => SameSavingsGoalDelete(g, deleted))
                .Select(g => g.Id)
                .ToHashSet();
            if (removedIds.Count == 0) continue;

            SavingsGoals.RemoveAll(g => removedIds.Contains(g.Id));
            _pendingNewSavingsGoals.RemoveAll(g => removedIds.Contains(g.Id));
            _pendingUpdatedSavingsGoals.RemoveAll(g => removedIds.Contains(g.Id));
            foreach (var id in removedIds.Where(id => id > 0))
            {
                _pendingDeletedSavingsGoalIds.RemoveAll(x => x == id);
                _pendingDeletedSavingsGoalIds.Add(id);
            }
        }
    }

    private async Task ApplyPersistedTripDeletesAsync()
    {
        var deletes = await db.GetPendingTripDeletesAsync();
        foreach (var deleted in deletes)
        {
            var removedIds = Trips
                .Where(t => SameTripDelete(t, deleted))
                .Select(t => t.Id)
                .ToHashSet();
            if (removedIds.Count == 0) continue;

            Trips.RemoveAll(t => removedIds.Contains(t.Id));
            _pendingNewTrips.RemoveAll(t => removedIds.Contains(t.Id));
            _pendingUpdatedTrips.RemoveAll(t => removedIds.Contains(t.Id));
            foreach (var id in removedIds.Where(id => id > 0))
            {
                _pendingDeletedTripIds.RemoveAll(x => x == id);
                _pendingDeletedTripIds.Add(id);
            }
        }
    }

    private Transaction? FindTransactionForOverride(Transaction updated)
    {
        if (!string.IsNullOrWhiteSpace(updated.UpTransactionId))
        {
            var byUpId = Transactions.FirstOrDefault(t =>
                string.Equals(t.UpTransactionId, updated.UpTransactionId, StringComparison.Ordinal));
            if (byUpId is not null) return byUpId;
        }

        var byId = Transactions.FirstOrDefault(t => t.Id == updated.Id);
        if (byId is not null) return byId;

        return Transactions.FirstOrDefault(t => SameTransactionSignature(t, updated));
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
            .ThenByDescending(t => t.UpSettledAt ?? DateTime.MinValue)
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

        AffordabilityGoalAmount = decimal.TryParse(GetSetting("AffordabilityAmount"), out var goalAmount) && goalAmount > 0
            ? goalAmount
            : 0m;
        AffordabilityGoalWeeks = int.TryParse(GetSetting("AffordabilityWeeks"), out var goalWeeks) && goalWeeks > 0
            ? goalWeeks
            : 4;
        AffordabilityGoalAccountName = GetSetting("AffordabilityAccountName") ?? string.Empty;

        AffordabilityMode = GetSetting("AffordabilityMode")
            ?? (AffordabilityGoalAmount > 0 ? "Savings" : "OneOff");

        AffordabilityInstallmentAmount = decimal.TryParse(GetSetting("AffordabilityInstallmentAmount"), out var instAmount) && instAmount > 0
            ? instAmount
            : 0m;
        AffordabilityInstallmentCount = int.TryParse(GetSetting("AffordabilityInstallments"), out var instCount) && instCount >= 2
            ? instCount
            : 4;
        AffordabilityInstallmentFrequencyWeeks = int.TryParse(GetSetting("AffordabilityInstallmentWeeks"), out var instFreq) && instFreq > 0
            ? instFreq
            : 2;

        AppLockEnabled = !string.IsNullOrEmpty(GetSetting(AppLockPinHashKey));
        if (!_lockStateInitialized)
        {
            _lockStateInitialized = true;
            IsUnlocked = !AppLockEnabled;
        }

        var budget = WeeklyBudgets.FirstOrDefault();
        if (budget is not null)
        {
            WeeklyIncome = budget.IncomeDollars;
            BudgetBills = budget.BillsDollars;
            BudgetEssentials = budget.EssentialsDollars;
            BudgetSavings = budget.SavingsDollars;
            BudgetUnplanned = budget.UnplannedDollars;
        }
        PlannedSavingsTransfers = CalculatePlannedSavingsTransfers();
        BudgetSavings = Math.Max(BudgetSavings, PlannedSavingsTransfers);
    }

    private decimal CalculatePlannedSavingsTransfers()
    {
        var goalTransfers = SavingsGoals.Sum(g => g.WeeklyContributionDollars);
        var tripTransfers = Trips.Sum(t => t.WeeklyContributionDollars);
        return Math.Round(goalTransfers + tripTransfers, 2);
    }

    public decimal GetAccountGoalWeeklyContribution(Account account)
    {
        if (account.TargetCents is null || account.TargetDate is null) return 0m;
        var current = Transactions.Where(t => t.AccountId == account.Id).Sum(t => t.AmountDollars);
        var starting = account.TargetStartingBalanceDollars ?? current;
        var remaining = Math.Max(account.TargetDollars!.Value - Math.Max(current, starting), 0m);
        var days = Math.Max((account.TargetDate.Value.Date - DateTime.Today).TotalDays, 1);
        var weeks = Math.Max((decimal)Math.Ceiling(days / 7d), 1m);
        return Math.Round(remaining / weeks, 2);
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

    private static DateTime ReverseDueDate(DateTime d, BillFrequency f) => f switch
    {
        BillFrequency.Weekly      => d.AddDays(-7),
        BillFrequency.Fortnightly => d.AddDays(-14),
        BillFrequency.Monthly     => d.AddMonths(-1),
        BillFrequency.Quarterly   => d.AddMonths(-3),
        BillFrequency.Yearly      => d.AddYears(-1),
        _                         => d.AddMonths(-1)
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

        // 4. If bill.DueDate is already in the next cycle (WPF paid today and
        //    bumped DueDate forward), the paid status lives one period back.
        //    Check prevDue before defaulting to unpaid.  Don't use bill.IsPaid
        //    — it's never auto-reset between cycles and would falsely show the
        //    NEW cycle as paid.
        //    Bounded to a brief grace window around prevDue — without this,
        //    a bill paid once kept inheriting "paid" for its entire next cycle
        //    (weeks/months), showing future occurrences as already paid.
        if (effectiveDue.Date > DateTime.Today)
        {
            var prevDue = ReverseDueDate(effectiveDue, bill.Frequency);
            if (Math.Abs((DateTime.Today - prevDue.Date).TotalDays) <= 3)
            {
                var prevStatus = BillStatuses
                    .FirstOrDefault(s => s.BillId == bill.Id && s.DueDate.Date == prevDue.Date);
                if (prevStatus is not null) return prevStatus.IsPaid;
                if (latest is not null)
                {
                    var daysToPrev = Math.Abs((latest.DueDate.Date - prevDue.Date).TotalDays);
                    if (daysToPrev <= 3) return latest.IsPaid;
                }
            }
        }
        return false;
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

    public (DateTime from, DateTime to) GetCurrentPayCycle()
    {
        const int cycleDays = 7;
        var today = DateTime.Today;
        var nextPayday = NextPayDate.Date;
        while (nextPayday < today)
            nextPayday = nextPayday.AddDays(cycleDays);

        var from = nextPayday == today ? today : nextPayday.AddDays(-cycleDays);
        var to = nextPayday == today ? today.AddDays(cycleDays - 1) : nextPayday.AddDays(-1);
        return (from, to);
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
            .ThenByDescending(t => t.UpSettledAt ?? DateTime.MinValue)
            .ThenByDescending(t => t.Id)
            .ToList();

    // ── Spending stats ─────────────────────────────────────────────────────────
    // Total spending (all categories) — used for dashboard/trend
    public decimal GetPeriodSpending()
    {
        var (from, to) = GetCurrentPeriod();
        return GetPeriodSpendingForPeriod(from, to);
    }

    public decimal GetPeriodSpendingForPeriod(DateTime from, DateTime to)
    {
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsInternalMovement(t) && !IsLent(t.Id))
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
                        && !IsLent(t.Id))
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
        return GetTopCategoriesForPeriod(from, to, n, excludeBills);
    }

    public List<(string Category, decimal Amount)> GetTopCategoriesForPeriod(DateTime from, DateTime to, int n = 5, bool excludeBills = false)
    {
        return Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsInternalMovement(t) && !IsLent(t.Id) && (!excludeBills || !IsBudgetedBillTransaction(t)))
            .GroupBy(t => t.CategoryName)
            .Select(g => (g.Key, g.Sum(t => Math.Abs(t.AmountDollars))))
            .OrderByDescending(x => x.Item2)
            .Take(n)
            .ToList();
    }

    // Default VAPID keypair bundled with the app so push works out of the box.
    // Matching private key is held in GitHub Actions secrets for bill-reminders.yml.
    private const string DefaultVapidPublicKey = "BMtvXPfYQjzAND9Kjp3uL5cUbmL9w_MxU1J1SOEFNLEEG8Ge2mUApMhQ3TvnlVPH46rheyXVPG5JcBVNTf1_YBc";

    public string VapidPublicKey
    {
        get
        {
            var saved = AppSettings.FirstOrDefault(s => s.Key == "VapidPublicKey")?.Value;
            return string.IsNullOrWhiteSpace(saved) ? DefaultVapidPublicKey : saved;
        }
    }

    public decimal GetCategoryLimitDollars(string categoryName)
    {
        var val = AppSettings.FirstOrDefault(s => s.Key == $"CategoryLimit:{categoryName}")?.Value;
        return int.TryParse(val, out var cents) ? cents / 100m : 0m;
    }

    // ── Subscriptions / recurring payments ────────────────────────────────────
    private const string IgnoredSubscriptionsSettingKey = "IgnoredSubscriptions";

    private List<string> GetIgnoredSubscriptions()
    {
        var json = GetSetting(IgnoredSubscriptionsSettingKey);
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public List<RecurringPayment> GetRecurringPayments()
    {
        var ignored = GetIgnoredSubscriptions().ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recurring = Transactions
            .Where(t => t.AmountCents < 0 && !IsInternalMovement(t) && !string.IsNullOrWhiteSpace(t.Description))
            .GroupBy(t => NormalizeRecurringDescription(t.Description))
            .Where(g => g.Count() >= 2)
            .Select(g => BuildRecurringPayment(g.OrderBy(t => t.Date).ToList()))
            .Where(row => row is not null)
            .Cast<RecurringPayment>()
            .Where(row => !ignored.Contains(row.Name))
            .OrderBy(row => row.NextExpected)
            .ThenBy(row => row.Name)
            .Take(30)
            .ToList();

        foreach (var row in recurring)
        {
            row.IsAlreadyBill = Bills.Any(b =>
                string.Equals(NormalizeRecurringDescription(b.Name), row.Name, StringComparison.OrdinalIgnoreCase));
        }

        return recurring;
    }

    public decimal SubscriptionWeeklyTotal => GetRecurringPayments().Sum(r => r.WeeklyAmount);

    public int SubscriptionsNotInBillsCount => GetRecurringPayments().Count(r => !r.IsAlreadyBill);

    public async Task IgnoreSubscriptionAsync(string name)
    {
        var ignored = GetIgnoredSubscriptions();
        if (!ignored.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ignored.Add(name);
            await SaveSettingAsync(IgnoredSubscriptionsSettingKey, System.Text.Json.JsonSerializer.Serialize(ignored));
        }
    }

    private static RecurringPayment? BuildRecurringPayment(IReadOnlyList<Transaction> transactions)
    {
        var gaps = transactions
            .Zip(transactions.Skip(1), (previous, next) => (next.Date.Date - previous.Date.Date).TotalDays)
            .Where(d => d > 0)
            .OrderBy(d => d)
            .ToList();

        if (gaps.Count == 0) return null;

        var medianGap = gaps[gaps.Count / 2];
        var (frequency, days) = GetRecurringFrequency(medianGap);
        if (days == 0) return null;

        var last = transactions[^1];
        var amounts = transactions.Select(t => Math.Abs(t.AmountDollars)).ToList();
        var averageAmount = Math.Round(amounts.Average(), 2);

        return new RecurringPayment
        {
            Name = NormalizeRecurringDescription(last.Description),
            Amount = Math.Abs(last.AmountDollars),
            AverageAmount = averageAmount,
            MinAmount = amounts.Min(),
            MaxAmount = amounts.Max(),
            WeeklyAmount = GetWeeklyAmount(averageAmount, frequency),
            Frequency = frequency,
            AccountName = last.AccountName,
            LastPaid = last.Date.Date,
            NextExpected = last.Date.Date.AddDays(days),
            TimesSeen = transactions.Count,
            CategoryName = string.IsNullOrWhiteSpace(last.CategoryName) ? "Misc" : last.CategoryName
        };
    }

    private static decimal GetWeeklyAmount(decimal amount, string frequency) => frequency switch
    {
        "Weekly" => Math.Round(amount, 2),
        "Fortnightly" => Math.Round(amount / 2m, 2),
        "Monthly" => Math.Round(amount * 12m / 52m, 2),
        "Quarterly" => Math.Round(amount * 4m / 52m, 2),
        "Yearly" => Math.Round(amount / 52m, 2),
        _ => 0
    };

    private static (string Frequency, int Days) GetRecurringFrequency(double medianGap) => medianGap switch
    {
        >= 5 and <= 9 => ("Weekly", 7),
        >= 12 and <= 17 => ("Fortnightly", 14),
        >= 26 and <= 35 => ("Monthly", 30),
        >= 80 and <= 100 => ("Quarterly", 91),
        >= 350 and <= 380 => ("Yearly", 365),
        _ => ("", 0)
    };

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

    // ── Data export ────────────────────────────────────────────────────────────
    public string BuildTransactionsCsv()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Date,Description,Account,Category,Amount\r\n");
        foreach (var t in Transactions.OrderBy(t => t.Date).ThenBy(t => t.Id))
        {
            sb.Append(t.Date.ToString("yyyy-MM-dd")).Append(',')
              .Append(CsvField(t.Description)).Append(',')
              .Append(CsvField(t.AccountName)).Append(',')
              .Append(CsvField(t.CategoryName)).Append(',')
              .Append(t.AmountDollars.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
              .Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CsvField(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    // ── App lock (PIN) ────────────────────────────────────────────────────────
    public bool TryUnlock(string pin)
    {
        var stored = GetSetting(AppLockPinHashKey);
        if (string.IsNullOrEmpty(stored) || HashPin(pin) != stored) return false;
        IsUnlocked = true;
        OnChange?.Invoke();
        return true;
    }

    public async Task SetPinAsync(string pin) =>
        await SaveSettingAsync(AppLockPinHashKey, HashPin(pin));

    public async Task DisablePinAsync() =>
        await SaveSettingAsync(AppLockPinHashKey, string.Empty);

    private static string HashPin(string pin) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppLockSalt + pin)));

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
            description.Contains("Swoosh Finance", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("State Penalties Enforcement", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Qantas Insurance", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Suncorp Insurance", StringComparison.OrdinalIgnoreCase);
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
            var score = spending == 0 ? 0 : (int)(necessary / spending * 100);
            var grade = spending == 0 ? "-" : score switch
            {
                100 => "A+", >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 50 => "D", _ => "F"
            };
            var color = spending == 0 ? "#6E7681" : score switch
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
            if (!txByDay.TryGetValue(date, out var dayTx) || dayTx.Count == 0) break;
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
        QueueUpdatedTransaction(t);
        await db.SetTransactionOverrideAsync(t);
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
        QueueUpdatedTransaction(t);
        await db.SetTransactionOverrideAsync(t);
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

    public async Task DeleteBillAsync(int id)
    {
        var deletedBill = Bills.FirstOrDefault(b => b.Id == id);
        var deleteIntent = deletedBill is null ? null : ToBillDelete(deletedBill);
        await RestoreMatchedBillAdjustmentsAsync(id);
        Bills.RemoveAll(b => b.Id == id);
        BillStatuses.RemoveAll(s => s.BillId == id);
        _pendingNewBills.RemoveAll(b => b.Id == id);
        _pendingUpdatedBills.RemoveAll(b => b.Id == id);
        _pendingBillStatuses.RemoveAll(s => s.BillId == id);
        if (id > 0)
        {
            _pendingDeletedBillIds.RemoveAll(x => x == id);
            _pendingDeletedBillIds.Add(id);
        }
        if (deleteIntent is not null)
        {
            _pendingDeletedBills.RemoveAll(d => SameBillDelete(d, deleteIntent));
            _pendingDeletedBills.Add(deleteIntent);
            await db.SetBillDeleteAsync(deleteIntent);
        }
        await db.DeleteAsync("bills", id);
        await db.ClearBillOverrideAsync(id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task<Debt> AddDebtAsync(Debt d)
    {
        var minId = Debts.Count > 0 ? Debts.Min(x => x.Id) : 0;
        d.Id = Math.Min(minId - 1, -1);
        Debts.Add(d);
        _pendingNewDebts.Add(d);
        await db.PutAsync("debts", d);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
        return d;
    }

    public async Task UpdateDebtAsync(Debt d)
    {
        var existing = Debts.FirstOrDefault(x => x.Id == d.Id);
        if (existing is null) return;
        existing.Name = d.Name;
        existing.BalanceCents = d.BalanceCents;
        existing.MinimumPaymentCents = d.MinimumPaymentCents;
        existing.PaymentPeriod = d.PaymentPeriod;
        existing.InterestRate = d.InterestRate;
        existing.OriginalBalanceCents = d.OriginalBalanceCents;
        await db.PutAsync("debts", existing);
        // Negative-id (not-yet-synced) debts are mutated in place via the
        // same object reference already queued in _pendingNewDebts.
        if (existing.Id > 0)
            QueueUpdatedDebt(existing);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    // Cascades like WPF's FK delete behaviors: DebtPayment.Debt -> Cascade
    // (remove payments tied to this debt) and Bill.Debt -> SetNull (unlink
    // any installment bill that was tracking this debt).
    public async Task DeleteDebtAsync(int id)
    {
        var deletedDebt = Debts.FirstOrDefault(d => d.Id == id);
        Debts.RemoveAll(d => d.Id == id);
        _pendingNewDebts.RemoveAll(d => d.Id == id);
        _pendingUpdatedDebts.RemoveAll(d => d.Id == id);

        foreach (var payment in DebtPayments.Where(p => p.DebtId == id).ToList())
        {
            DebtPayments.Remove(payment);
            await db.DeleteAsync("debtPayments", payment.Id);
            _pendingNewDebtPayments.RemoveAll(x => x.Id == payment.Id);
            if (payment.Id > 0) _pendingDeletedDebtPaymentIds.Add(payment.Id);
        }

        foreach (var bill in Bills.Where(b => b.DebtId == id))
        {
            bill.DebtId = null;
            await db.PutAsync("bills", bill);
            if (bill.Id > 0 && !_pendingUpdatedBills.Any(x => x.Id == bill.Id))
                _pendingUpdatedBills.Add(bill);
        }

        if (id > 0) _pendingDeletedDebtIds.Add(id);
        if (deletedDebt is not null && id > 0)
            await db.SetDebtDeleteAsync(deletedDebt);
        await db.DeleteAsync("debts", id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task<SavingsGoal> AddSavingsGoalAsync(SavingsGoal g)
    {
        var minId = SavingsGoals.Count > 0 ? SavingsGoals.Min(x => x.Id) : 0;
        g.Id = Math.Min(minId - 1, -1);
        SavingsGoals.Add(g);
        _pendingNewSavingsGoals.Add(g);
        await db.PutAsync("savingsGoals", g);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
        return g;
    }

    public async Task UpdateSavingsGoalAsync(SavingsGoal g)
    {
        var existing = SavingsGoals.FirstOrDefault(x => x.Id == g.Id);
        if (existing is null) return;
        existing.Name = g.Name;
        existing.TargetCents = g.TargetCents;
        existing.CurrentCents = g.CurrentCents;
        existing.WeeklyContributionCents = g.WeeklyContributionCents;
        existing.TargetDate = g.TargetDate;
        await db.PutAsync("savingsGoals", existing);
        // Negative-id (not-yet-synced) goals are mutated in place via the
        // same object reference already queued in _pendingNewSavingsGoals.
        if (existing.Id > 0)
            QueueUpdatedSavingsGoal(existing);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task DeleteSavingsGoalAsync(int id)
    {
        var deletedGoal = SavingsGoals.FirstOrDefault(g => g.Id == id);
        SavingsGoals.RemoveAll(g => g.Id == id);
        _pendingNewSavingsGoals.RemoveAll(g => g.Id == id);
        _pendingUpdatedSavingsGoals.RemoveAll(g => g.Id == id);
        if (id > 0) _pendingDeletedSavingsGoalIds.Add(id);
        if (deletedGoal is not null && id > 0)
            await db.SetSavingsGoalDeleteAsync(deletedGoal);
        await db.DeleteAsync("savingsGoals", id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task<Trip> AddTripAsync(Trip t)
    {
        var minId = Trips.Count > 0 ? Trips.Min(x => x.Id) : 0;
        t.Id = Math.Min(minId - 1, -1);
        Trips.Add(t);
        _pendingNewTrips.Add(t);
        await db.PutAsync("trips", t);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
        return t;
    }

    public async Task UpdateTripAsync(Trip t)
    {
        var existing = Trips.FirstOrDefault(x => x.Id == t.Id);
        if (existing is null) return;
        existing.Name = t.Name;
        existing.Destination = t.Destination;
        existing.Notes = t.Notes;
        existing.StartDate = t.StartDate;
        existing.EndDate = t.EndDate;
        existing.SavingsAccountId = t.SavingsAccountId;
        existing.WeeklyContributionCents = t.WeeklyContributionCents;
        existing.Itinerary = t.Itinerary;
        existing.Checklist = t.Checklist;
        existing.BudgetItems = t.BudgetItems;
        await db.PutAsync("trips", existing);
        // Negative-id (not-yet-synced) trips are mutated in place via the
        // same object reference already queued in _pendingNewTrips.
        if (existing.Id > 0)
            QueueUpdatedTrip(existing);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task DeleteTripAsync(int id)
    {
        var deletedTrip = Trips.FirstOrDefault(t => t.Id == id);
        Trips.RemoveAll(t => t.Id == id);
        _pendingNewTrips.RemoveAll(t => t.Id == id);
        _pendingUpdatedTrips.RemoveAll(t => t.Id == id);
        if (id > 0) _pendingDeletedTripIds.Add(id);
        if (deletedTrip is not null && id > 0)
            await db.SetTripDeleteAsync(deletedTrip);
        await db.DeleteAsync("trips", id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    // Used when a new bill is created from the "Add to Budget as a Bill"
    // installment-plan flow: also create a matching Debt so the remaining
    // balance shows progress as installments are paid off, same as other
    // bill-linked debts.
    public async Task<Bill> AddInstallmentBillAsync(Bill b, decimal totalAmountDollars)
    {
        await AddBillAsync(b);

        var debt = await AddDebtAsync(new Debt
        {
            Name = b.Name,
            BalanceDollars = totalAmountDollars,
            OriginalBalanceDollars = totalAmountDollars,
            MinimumPaymentDollars = b.AmountDollars,
            PaymentPeriod = b.Frequency switch
            {
                BillFrequency.Weekly => "Weekly",
                BillFrequency.Fortnightly => "Fortnightly",
                BillFrequency.Monthly => "Monthly",
                BillFrequency.Quarterly => "Quarterly",
                BillFrequency.Yearly => "Yearly",
                _ => "Weekly"
            }
        });

        b.DebtId = debt.Id;
        await db.PutAsync("bills", b);
        OnChange?.Invoke();
        return b;
    }

    public async Task SetAccountGoalAsync(int accountId, decimal targetDollars, DateTime targetDate)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        account.TargetDollars = targetDollars;
        account.TargetDate = targetDate;
        account.TargetStartDate = DateTime.Today;
        account.TargetStartingBalanceDollars = GetAccountBalance(accountId);
        await db.PutAsync("accounts", account);
        QueueUpdatedAccount(account);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    // Edits an existing account goal's target without resetting the
    // progress-tracking anchors (TargetStartDate/TargetStartingBalance).
    public async Task UpdateAccountGoalAsync(int accountId, decimal targetDollars, DateTime? targetDate)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        account.TargetDollars = targetDollars;
        account.TargetDate = targetDate;
        await db.PutAsync("accounts", account);
        QueueUpdatedAccount(account);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task ClearAccountGoalAsync(int accountId)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        account.TargetCents = null;
        account.TargetDate = null;
        account.TargetStartDate = null;
        account.TargetStartingBalanceCents = null;
        await db.PutAsync("accounts", account);
        QueueUpdatedAccount(account);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    private void QueueUpdatedAccount(Account a)
    {
        _pendingUpdatedAccounts.RemoveAll(x => x.Id == a.Id);
        _pendingUpdatedAccounts.Add(a);
    }

    private void QueueUpdatedDebt(Debt d)
    {
        _pendingUpdatedDebts.RemoveAll(x => x.Id == d.Id);
        _pendingUpdatedDebts.Add(d);
    }

    private void QueueUpdatedSavingsGoal(SavingsGoal g)
    {
        _pendingUpdatedSavingsGoals.RemoveAll(x => x.Id == g.Id);
        _pendingUpdatedSavingsGoals.Add(g);
    }

    private void QueueUpdatedTrip(Trip t)
    {
        _pendingUpdatedTrips.RemoveAll(x => x.Id == t.Id);
        _pendingUpdatedTrips.Add(t);
    }

    // Mirrors WPF's DebtPaymentMatcher.ApplyBillDebtPaymentStatus: when a bill
    // linked to a debt (by DebtId, or by fuzzy name match as a fallback) is
    // marked paid/unpaid, adjust the debt balance and record/remove a
    // DebtPayment so progress shows on the Accounts page "same as is already there".
    private async Task ApplyBillDebtPaymentAsync(Bill bill, DateTime dueDate, bool isPaid)
    {
        var paymentId = $"bill:{bill.Id}:{dueDate:yyyyMMdd}";
        // Phone-created payment uses the "bill:…" format.
        var phonePayment = DebtPayments.FirstOrDefault(p => p.UpTransactionId == paymentId);

        if (!isPaid)
        {
            // Only undo a payment the phone itself created — don't touch WPF's
            // Up Bank payments (different UpTransactionId format).
            if (phonePayment is null) return;

            var existingDebt = Debts.FirstOrDefault(d => d.Id == phonePayment.DebtId);
            if (existingDebt is not null)
            {
                existingDebt.BalanceCents += phonePayment.AmountCents;
                await db.PutAsync("debts", existingDebt);
                QueueUpdatedDebt(existingDebt);
            }

            DebtPayments.Remove(phonePayment);
            await db.DeleteAsync("debtPayments", phonePayment.Id);
            _pendingNewDebtPayments.RemoveAll(p => p.UpTransactionId == paymentId);
            if (phonePayment.Id > 0)
                _pendingDeletedDebtPaymentIds.Add(phonePayment.Id);
            return;
        }

        if (phonePayment is not null) return;

        var debt = bill.DebtId is { } debtId
            ? Debts.FirstOrDefault(d => d.Id == debtId)
            : FindMatchingDebt(bill.Name);
        if (debt is null) return;

        var paymentCents = Math.Abs(bill.AmountCents);

        // WPF uses Up Bank transaction IDs (not the "bill:…" format) and pays
        // the actual bank amount (which may differ from bill.AmountCents for
        // partial/overpayments). Check by debt + date window only; don't
        // compare amounts since bill amount ≠ transaction amount is common.
        var halfPeriodDays = bill.Frequency switch
        {
            BillFrequency.Weekly      => 4,
            BillFrequency.Fortnightly => 7,
            BillFrequency.Monthly     => 10,
            BillFrequency.Quarterly   => 14,
            BillFrequency.Yearly      => 30,
            _                         => 7
        };
        var wpfPaymentExists = DebtPayments.Any(p =>
            p.DebtId == debt.Id &&
            p.UpTransactionId != paymentId &&
            Math.Abs((p.PaidOn.Date - dueDate.Date).TotalDays) <= halfPeriodDays);
        if (wpfPaymentExists) return;

        debt.BalanceCents = Math.Max(0, debt.BalanceCents - paymentCents);
        await db.PutAsync("debts", debt);
        QueueUpdatedDebt(debt);

        var minId = DebtPayments.Count > 0 ? DebtPayments.Min(x => x.Id) : 0;
        var payment = new DebtPayment
        {
            Id = Math.Min(minId - 1, -1),
            DebtId = debt.Id,
            UpTransactionId = paymentId,
            AmountCents = paymentCents,
            PaidOn = dueDate.Date,
            Description = bill.Name
        };
        DebtPayments.Add(payment);
        await db.PutAsync("debtPayments", payment);
        _pendingNewDebtPayments.Add(payment);
    }

    // Fuzzy debt match, mirroring WPF's DebtPaymentMatcher.FindMatchingDebt:
    // matches a bill/transaction description against Debt.UpPaymentMatchText
    // (or Debt.Name as a fallback) after stripping non-alphanumeric chars.
    private Debt? FindMatchingDebt(string description)
    {
        var normalizedDescription = NormalizeForMatch(description);
        return Debts
            .Select(debt => new
            {
                Debt = debt,
                MatchTexts = GetDebtMatchTexts(debt)
                    .Where(m => NormalizeForMatch(m) != "")
                    .ToList()
            })
            .Where(c => c.MatchTexts.Any(m => normalizedDescription.Contains(NormalizeForMatch(m))))
            .OrderByDescending(c => c.MatchTexts.Max(m => NormalizeForMatch(m).Length))
            .Select(c => c.Debt)
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetDebtMatchTexts(Debt debt)
    {
        var configured = (debt.UpPaymentMatchText ?? "")
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return configured.Length > 0 ? configured : new[] { debt.Name };
    }

    private static string NormalizeForMatch(string value) =>
        new(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public async Task UpdateTransactionCategoryAsync(int transactionId, int categoryId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (t is null) return;
        t.CategoryId = categoryId;
        t.CategoryName = Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "";
        await db.PutAsync("transactions", t);
        QueueUpdatedTransaction(t);
        await db.SetTransactionOverrideAsync(t);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task DeleteTransactionAsync(int id)
    {
        var deleted = Transactions.FirstOrDefault(t => t.Id == id);
        Transactions.RemoveAll(t => t.Id == id);
        var isGeneratedBalanceAdjustment = deleted is not null && IsGeneratedBalanceAdjustment(deleted);
        if (id > 0 && !isGeneratedBalanceAdjustment)
            _pendingDeletedTransactionIds.Add(id);

        if (deleted is not null && !isGeneratedBalanceAdjustment)
        {
            var deleteIntent = ToTransactionDelete(deleted);
            _pendingDeletedTransactions.RemoveAll(t => SameTransactionDelete(t, deleteIntent));
            _pendingDeletedTransactions.Add(deleteIntent);
            await db.SetTransactionDeleteAsync(deleteIntent);
        }
        await db.DeleteAsync("transactions", id);
        await db.ClearTransactionOverrideAsync(id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    private async Task RestoreMatchedBillAdjustmentsAsync(int billId)
    {
        var statuses = BillStatuses
            .Where(s => s.BillId == billId &&
                s.MatchedTransactionId is not null &&
                !string.IsNullOrWhiteSpace(s.OriginalTransactionDescription))
            .ToList();
        foreach (var status in statuses)
        {
            var transaction = Transactions.FirstOrDefault(t => t.Id == status.MatchedTransactionId);
            if (transaction is null) continue;

            transaction.Description = status.OriginalTransactionDescription!;
            if (status.OriginalTransactionCategoryId is not null)
            {
                transaction.CategoryId = status.OriginalTransactionCategoryId.Value;
                transaction.CategoryName = Categories.FirstOrDefault(c => c.Id == transaction.CategoryId)?.Name ?? transaction.CategoryName;
            }

            transaction.TransferId = Guid.TryParse(status.OriginalTransactionTransferId, out var transferId)
                ? transferId
                : null;
            await db.PutAsync("transactions", transaction);
            QueueUpdatedTransaction(transaction);
            await db.SetTransactionOverrideAsync(transaction);
        }
    }

    private static bool IsGeneratedBalanceAdjustment(Transaction transaction) =>
        transaction.UpTransactionId is null &&
        (transaction.TransferId == Guid.Empty ||
            transaction.Description.Equals("Up balance adjustment", StringComparison.OrdinalIgnoreCase) ||
            transaction.CategoryName.Equals("Balance Adjustment", StringComparison.OrdinalIgnoreCase));

    public async Task MarkBillPaidAsync(int billId, bool paid)
    {
        var bill = Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null) return;
        bill.IsPaid = paid;
        await db.PutAsync("bills", bill);

        var status = GetOrCreateCurrentStatus(bill);
        status.IsPaid = paid;
        status.PaidOn = paid ? DateTime.Now : null;

        _pendingBillStatuses.RemoveAll(s => s.BillId == status.BillId && s.DueDate.Date == status.DueDate.Date);
        _pendingBillStatuses.Add(status);
        await db.PutAsync("billOccurrenceStatuses", status);
        // Persist the override so it survives sync's clearAll and app restarts
        await db.SetBillOverrideAsync(billId, paid);
        await ApplyBillDebtPaymentAsync(bill, status.DueDate, paid);
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
        try { await Task.Delay(TimeSpan.FromMilliseconds(500), token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested) return;

        // If the initial auto-sync is still running, wait for it to finish
        // (up to 25 s — one tick per second).  This prevents edit syncs from
        // being silently dropped when the user makes a change in the first few
        // seconds after launch.  SyncAndReloadAsync also has a 25-second watchdog
        // that force-resets _syncInProgress if iOS suspended the app mid-request.
        for (int i = 0; i < 25 && _syncInProgress; i++)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), token); }
            catch (OperationCanceledException) { return; }
            if (token.IsCancellationRequested) return;
        }

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
        var category = ResolveCategory(updated.CategoryName, updated.CategoryId, updated.AmountCents);
        target.CategoryId = category.Id;
        target.CategoryName = category.Name;
        target.TransferId = updated.TransferId;
        target.UpTransactionId = updated.UpTransactionId;
        target.IsUnnecessary = updated.IsUnnecessary;
    }

    private Category ResolveCategory(string? categoryName, int categoryId, int amountCents)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var byName = Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }

        var byId = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (byId is not null) return byId;

        var fallbackName = amountCents > 0 ? "Income" : "Misc";
        var fallback = Categories.FirstOrDefault(c => string.Equals(c.Name, fallbackName, StringComparison.OrdinalIgnoreCase));
        if (fallback is not null) return fallback;

        var newId = Math.Min(Categories.Select(c => c.Id).DefaultIfEmpty(0).Min() - 1, -1);
        var created = new Category
        {
            Id = newId,
            Name = fallbackName,
            Type = amountCents > 0 ? CategoryType.Income : CategoryType.Expense
        };
        Categories.Add(created);
        return created;
    }

    private void QueueUpdatedTransaction(Transaction transaction)
    {
        _pendingUpdatedTransactions.RemoveAll(t => t.Id == transaction.Id);
        _pendingUpdatedTransactions.Add(transaction);
    }

    private static TransactionEdit ToTransactionEdit(Transaction transaction) => new()
    {
        Id = transaction.Id,
        UpTransactionId = transaction.UpTransactionId,
        Date = transaction.Date,
        Description = transaction.Description,
        AmountCents = transaction.AmountCents,
        AccountId = transaction.AccountId,
        AccountName = transaction.AccountName,
        CategoryId = transaction.CategoryId,
        CategoryName = transaction.CategoryName,
        TransferId = transaction.TransferId,
        IsUnnecessary = transaction.IsUnnecessary
    };

    private static TransactionDelete ToTransactionDelete(Transaction transaction) => new()
    {
        Id = transaction.Id,
        UpTransactionId = transaction.UpTransactionId,
        Date = transaction.Date,
        Description = transaction.Description,
        AmountCents = transaction.AmountCents
    };

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

    private static bool SameBillDelete(BillDelete left, BillDelete right)
    {
        if (left.Id > 0 && right.Id > 0 && left.Id == right.Id) return true;
        if (left.AmountCents != right.AmountCents) return false;
        if (left.Frequency != right.Frequency) return false;
        if (!string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        var leftAccount = string.IsNullOrWhiteSpace(left.AccountName) ? left.AccountId.ToString() : left.AccountName.Trim();
        var rightAccount = string.IsNullOrWhiteSpace(right.AccountName) ? right.AccountId.ToString() : right.AccountName.Trim();
        if (!string.Equals(leftAccount, rightAccount, StringComparison.OrdinalIgnoreCase) && left.AccountId != right.AccountId) return false;
        return Math.Abs((left.DueDate.Date - right.DueDate.Date).TotalDays) <= 7;
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

    private static bool SameTransactionDelete(TransactionDelete left, TransactionDelete right)
    {
        if (!string.IsNullOrWhiteSpace(left.UpTransactionId) || !string.IsNullOrWhiteSpace(right.UpTransactionId))
        {
            return string.Equals(left.UpTransactionId, right.UpTransactionId, StringComparison.Ordinal);
        }

        return SameTransactionSignature(left.Date, left.Description, left.AmountCents, right.Date, right.Description, right.AmountCents);
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
            // Persisted separately from the in-memory queue so a setting change
            // (e.g. payday) survives an app restart that cuts off the debounced
            // push, instead of silently being lost and reverted by the next pull.
            await db.SetSettingOverrideAsync(setting);
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
        foreach (var deleted in push.DeletedTransactions)
        {
            var existing = FindPayloadTransaction(cloud.Transactions, deleted);
            if (existing is not null) cloud.Transactions.Remove(existing);
        }
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
        foreach (var edit in push.TransactionEdits)
        {
            var existing = FindPayloadTransaction(cloud.Transactions, edit);
            if (existing is null) continue;
            existing.Description = edit.Description;
            existing.AmountCents = edit.AmountCents;
            existing.Date = edit.Date;
            existing.AccountId = edit.AccountId;
            existing.CategoryId = ResolvePayloadCategoryId(cloud.Categories, edit.CategoryName, edit.CategoryId, edit.AmountCents);
            existing.TransferId = edit.TransferId;
            existing.UpTransactionId = edit.UpTransactionId;
            existing.IsUnnecessary = edit.IsUnnecessary;
        }
        cloud.Transactions.AddRange(push.NewTransactions);

        cloud.Bills.RemoveAll(b => push.DeletedBillIds.Contains(b.Id));
        foreach (var deleted in push.DeletedBills)
        {
            cloud.Bills.RemoveAll(b => SameBillDelete(b, deleted));
        }
        var remainingBillIds = cloud.Bills.Select(b => b.Id).ToHashSet();
        cloud.BillOccurrenceStatuses.RemoveAll(s => push.DeletedBillIds.Contains(s.BillId));
        cloud.BillOccurrenceStatuses.RemoveAll(s => !remainingBillIds.Contains(s.BillId));

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

        cloud.Debts.RemoveAll(d => push.DeletedDebtIds.Contains(d.Id));
        cloud.DebtPayments.RemoveAll(p => push.DeletedDebtIds.Contains(p.DebtId));
        foreach (var bill in cloud.Bills.Where(b => b.DebtId.HasValue && push.DeletedDebtIds.Contains(b.DebtId.Value)))
            bill.DebtId = null;

        foreach (var u in push.UpdatedDebts)
        {
            var existing = cloud.Debts.FirstOrDefault(d => d.Id == u.Id);
            if (existing is not null)
            {
                existing.Name = u.Name;
                existing.BalanceCents = u.BalanceCents;
                existing.MinimumPaymentCents = u.MinimumPaymentCents;
                existing.PaymentPeriod = u.PaymentPeriod;
                existing.InterestRate = u.InterestRate;
                existing.OriginalBalanceCents = u.OriginalBalanceCents;
            }
        }
        cloud.Debts.AddRange(push.NewDebts);

        foreach (var payment in push.NewDebtPayments)
        {
            var exists = !string.IsNullOrWhiteSpace(payment.UpTransactionId)
                ? cloud.DebtPayments.Any(p => string.Equals(p.UpTransactionId, payment.UpTransactionId, StringComparison.Ordinal))
                : cloud.DebtPayments.Any(p => p.Id == payment.Id);
            if (!exists)
            {
                cloud.DebtPayments.Add(payment);
            }
        }
        cloud.DebtPayments.RemoveAll(p => push.DeletedDebtPaymentIds.Contains(p.Id));

        foreach (var u in push.UpdatedAccounts)
        {
            var existing = cloud.Accounts.FirstOrDefault(a => a.Id == u.Id);
            if (existing is not null)
            {
                existing.TargetCents = u.TargetCents;
                existing.TargetDate = u.TargetDate;
                existing.TargetStartDate = u.TargetStartDate;
                existing.TargetStartingBalanceCents = u.TargetStartingBalanceCents;
            }
        }

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

        cloud.SavingsGoals.RemoveAll(g => push.DeletedSavingsGoalIds.Contains(g.Id));
        foreach (var u in push.UpdatedSavingsGoals)
        {
            var existing = cloud.SavingsGoals.FirstOrDefault(g => g.Id == u.Id);
            if (existing is not null)
            {
                existing.Name = u.Name;
                existing.TargetCents = u.TargetCents;
                existing.CurrentCents = u.CurrentCents;
                existing.WeeklyContributionCents = u.WeeklyContributionCents;
                existing.TargetDate = u.TargetDate;
            }
        }
        cloud.SavingsGoals.AddRange(push.NewSavingsGoals);

        cloud.Trips.RemoveAll(t => push.DeletedTripIds.Contains(t.Id));
        foreach (var u in push.UpdatedTrips)
        {
            var existing = cloud.Trips.FirstOrDefault(t => t.Id == u.Id);
            if (existing is not null)
            {
                existing.Name = u.Name;
                existing.Destination = u.Destination;
                existing.Notes = u.Notes;
                existing.StartDate = u.StartDate;
                existing.EndDate = u.EndDate;
                existing.SavingsAccountId = u.SavingsAccountId;
                existing.WeeklyContributionCents = u.WeeklyContributionCents;
                existing.Itinerary = u.Itinerary;
                existing.Checklist = u.Checklist;
                existing.BudgetItems = u.BudgetItems;
            }
        }
        cloud.Trips.AddRange(push.NewTrips);

        cloud.SyncedAt = DateTime.UtcNow;
        return cloud;
    }

    private static Transaction? FindPayloadTransaction(List<Transaction> transactions, TransactionEdit edit)
    {
        if (!string.IsNullOrWhiteSpace(edit.UpTransactionId))
        {
            var byUpId = transactions.FirstOrDefault(t =>
                string.Equals(t.UpTransactionId, edit.UpTransactionId, StringComparison.Ordinal));
            if (byUpId is not null) return byUpId;
        }

        return transactions.FirstOrDefault(t => t.Id == edit.Id) ??
            transactions.FirstOrDefault(t => SameTransactionSignature(t.Date, t.Description, t.AmountCents, edit.Date, edit.Description, edit.AmountCents));
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

    private static int ResolvePayloadCategoryId(List<Category> categories, string categoryName, int categoryId, int amountCents)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var byName = categories.FirstOrDefault(c => string.Equals(c.Name, categoryName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName.Id;
        }

        if (categories.Any(c => c.Id == categoryId)) return categoryId;
        var fallback = categories.FirstOrDefault(c => string.Equals(c.Name, amountCents > 0 ? "Income" : "Misc", StringComparison.OrdinalIgnoreCase));
        return fallback?.Id ?? categoryId;
    }

    public async Task SyncAndReloadAsync()
    {
        // Re-entrancy guard — but with a watchdog: if iOS suspended the app while
        // a request was in-flight the `finally` never ran and _syncInProgress is
        // permanently true.  Self-heal after the Supabase timeout (20s) plus buffer.
        if (_syncInProgress)
        {
            if ((DateTime.UtcNow - _syncStartedAt).TotalSeconds > 25)
                _syncInProgress = false;
            else
                return;
        }
        _syncInProgress = true;
        _syncStartedAt  = DateTime.UtcNow;
        try
        {
            // Push any phone-side changes — Wi-Fi first, Supabase as fallback
            var beforeTransactions = Transactions.Select(t => t.Id).ToHashSet();
            var beforeBills = Bills.Select(b => b.Id).ToHashSet();
            var beforeDebts = Debts.ToDictionary(d => d.Id, d => d.BalanceCents);
            var beforeDebtPayments = DebtPayments
                .Select(p => string.IsNullOrWhiteSpace(p.UpTransactionId) ? $"id:{p.Id}" : $"up:{p.UpTransactionId}")
                .ToHashSet();

            PushPayload? sentPush = null;

            if (HasPendingChanges)
            {
                var push = new PushPayload
                {
                    NewTransactions = new List<Transaction>(_pendingNewTransactions),
                    UpdatedTransactions = new List<Transaction>(_pendingUpdatedTransactions),
                    DeletedTransactionIds = new List<int>(_pendingDeletedTransactionIds),
                    TransactionEdits = _pendingUpdatedTransactions.Select(ToTransactionEdit).ToList(),
                    DeletedTransactions = new List<TransactionDelete>(_pendingDeletedTransactions),
                    UpdatedBillStatuses = new List<BillOccurrenceStatus>(_pendingBillStatuses),
                    NewBills = new List<Bill>(_pendingNewBills),
                    UpdatedBills = new List<Bill>(_pendingUpdatedBills),
                    DeletedBillIds = new List<int>(_pendingDeletedBillIds),
                    DeletedBills = new List<BillDelete>(_pendingDeletedBills),
                    NewDebts = new List<Debt>(_pendingNewDebts),
                    UpdatedDebts = new List<Debt>(_pendingUpdatedDebts),
                    DeletedDebtIds = new List<int>(_pendingDeletedDebtIds),
                    NewDebtPayments = new List<DebtPayment>(_pendingNewDebtPayments),
                    DeletedDebtPaymentIds = new List<int>(_pendingDeletedDebtPaymentIds),
                    UpdatedAccounts = new List<Account>(_pendingUpdatedAccounts),
                    UpdatedSettings = new List<AppSetting>(_pendingUpdatedSettings),
                    NewSavingsGoals = new List<SavingsGoal>(_pendingNewSavingsGoals),
                    UpdatedSavingsGoals = new List<SavingsGoal>(_pendingUpdatedSavingsGoals),
                    DeletedSavingsGoalIds = new List<int>(_pendingDeletedSavingsGoalIds),
                    NewTrips = new List<Trip>(_pendingNewTrips),
                    UpdatedTrips = new List<Trip>(_pendingUpdatedTrips),
                    DeletedTripIds = new List<int>(_pendingDeletedTripIds)
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

                // Consider the push successful if either phone_push (Supabase) or the PC
                // received it — both give WPF a path to reconcile.  PushFullSyncAsync
                // (the direct finance_sync update) is best-effort: failing it should not
                // permanently block the pending-changes queue or leave the badge stuck.
                var pushed = pushedToCloud || pushedToPc;
                if (pushed)
                {
                    sentPush = push;
                    foreach (var t in push.UpdatedTransactions)
                        await db.ClearTransactionOverrideAsync(t.Id);
                    foreach (var d in push.DeletedTransactions)
                        await db.ClearTransactionDeleteAsync(PendingTransactionDelete.GetStableId(d));
                    foreach (var s in push.UpdatedBillStatuses)
                        await db.ClearBillOverrideAsync(s.BillId);
                    foreach (var id in push.DeletedDebtIds.Where(id => id > 0))
                        await db.ClearDebtDeleteAsync(id);
                    foreach (var id in push.DeletedSavingsGoalIds.Where(id => id > 0))
                        await db.ClearSavingsGoalDeleteAsync(id);
                    foreach (var id in push.DeletedTripIds.Where(id => id > 0))
                        await db.ClearTripDeleteAsync(id);
                    foreach (var s in push.UpdatedSettings)
                        await db.ClearSettingOverrideAsync(s.Key);
                    // Remove only what was actually sent (by count, not Clear) —
                    // an edit made while this push was in flight appends to
                    // these lists and must survive for the next sync.
                    _pendingNewTransactions.RemoveRange(0, push.NewTransactions.Count);
                    _pendingUpdatedTransactions.RemoveRange(0, push.UpdatedTransactions.Count);
                    _pendingDeletedTransactionIds.RemoveRange(0, push.DeletedTransactionIds.Count);
                    _pendingDeletedTransactions.RemoveRange(0, push.DeletedTransactions.Count);
                    _pendingBillStatuses.RemoveRange(0, push.UpdatedBillStatuses.Count);
                    _pendingNewBills.RemoveRange(0, push.NewBills.Count);
                    _pendingUpdatedBills.RemoveRange(0, push.UpdatedBills.Count);
                    _pendingDeletedBillIds.RemoveRange(0, push.DeletedBillIds.Count);
                    _pendingDeletedBills.RemoveRange(0, push.DeletedBills.Count);
                    _pendingNewDebts.RemoveRange(0, push.NewDebts.Count);
                    _pendingUpdatedDebts.RemoveRange(0, push.UpdatedDebts.Count);
                    _pendingDeletedDebtIds.RemoveRange(0, push.DeletedDebtIds.Count);
                    _pendingNewDebtPayments.RemoveRange(0, push.NewDebtPayments.Count);
                    _pendingDeletedDebtPaymentIds.RemoveRange(0, push.DeletedDebtPaymentIds.Count);
                    _pendingUpdatedAccounts.RemoveRange(0, push.UpdatedAccounts.Count);
                    _pendingUpdatedSettings.RemoveRange(0, push.UpdatedSettings.Count);
                    _pendingNewSavingsGoals.RemoveRange(0, push.NewSavingsGoals.Count);
                    _pendingUpdatedSavingsGoals.RemoveRange(0, push.UpdatedSavingsGoals.Count);
                    _pendingDeletedSavingsGoalIds.RemoveRange(0, push.DeletedSavingsGoalIds.Count);
                    _pendingNewTrips.RemoveRange(0, push.NewTrips.Count);
                    _pendingUpdatedTrips.RemoveRange(0, push.UpdatedTrips.Count);
                    _pendingDeletedTripIds.RemoveRange(0, push.DeletedTripIds.Count);
                }
            }

            // Pull data — cloud first, then local Wi-Fi
            var ok = await sync.AutoSyncAsync();
            if (ok)
            {
                await LoadAsync();
                LastSyncChangeSummary = BuildSyncChangeSummary(beforeTransactions, beforeBills, beforeDebts, beforeDebtPayments);
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
        _pendingDeletedTransactions.Clear();
        _pendingBillStatuses.Clear();
        _pendingNewBills.Clear();
        _pendingUpdatedBills.Clear();
        _pendingDeletedBillIds.Clear();
        _pendingDeletedBills.Clear();
        _pendingNewDebts.Clear();
        _pendingUpdatedDebts.Clear();
        _pendingDeletedDebtIds.Clear();
        _pendingNewDebtPayments.Clear();
        _pendingDeletedDebtPaymentIds.Clear();
        _pendingUpdatedAccounts.Clear();
        _pendingUpdatedSettings.Clear();
        _pendingNewSavingsGoals.Clear();
        _pendingUpdatedSavingsGoals.Clear();
        _pendingDeletedSavingsGoalIds.Clear();
        _pendingNewTrips.Clear();
        _pendingUpdatedTrips.Clear();
        _pendingDeletedTripIds.Clear();
        await db.ClearBillOverridesAsync();
        await db.ClearBillDeletesAsync();
        await db.ClearDebtDeletesAsync();
        await db.ClearSavingsGoalDeletesAsync();
        await db.ClearTripDeletesAsync();
        OnChange?.Invoke();
    }

    private string BuildSyncChangeSummary(
        HashSet<int> beforeTransactions,
        HashSet<int> beforeBills,
        Dictionary<int, int> beforeDebts,
        HashSet<string> beforeDebtPayments)
    {
        var newTransactions = Transactions.Count(t => !beforeTransactions.Contains(t.Id));
        var newBills = Bills.Count(b => !beforeBills.Contains(b.Id));
        var newDebtPayments = DebtPayments.Count(p =>
        {
            var key = string.IsNullOrWhiteSpace(p.UpTransactionId) ? $"id:{p.Id}" : $"up:{p.UpTransactionId}";
            return !beforeDebtPayments.Contains(key);
        });
        var debtBalanceDelta = Debts.Sum(d => beforeDebts.GetValueOrDefault(d.Id, d.BalanceCents) - d.BalanceCents) / 100m;

        var parts = new List<string>();
        if (newTransactions > 0) parts.Add($"{newTransactions} new transaction{(newTransactions == 1 ? "" : "s")}");
        if (newBills > 0) parts.Add($"{newBills} new bill{(newBills == 1 ? "" : "s")}");
        if (newDebtPayments > 0) parts.Add($"{newDebtPayments} debt payment{(newDebtPayments == 1 ? "" : "s")}");
        if (Math.Abs(debtBalanceDelta) >= 0.01m)
        {
            parts.Add(debtBalanceDelta > 0
                ? $"{debtBalanceDelta:C} debt paid down"
                : $"{Math.Abs(debtBalanceDelta):C} added to debt balances");
        }

        return parts.Count == 0
            ? $"No major record changes from the last sync ({DateTime.Now:HH:mm})."
            : $"Last sync ({DateTime.Now:HH:mm}) brought in {string.Join(", ", parts)}.";
    }

    public async Task RepairPendingSyncAsync()
    {
        _syncDebounceCts?.Cancel();
        await MarkPendingChangesSyncedAsync();
        await db.ClearTransactionOverridesAsync();
        await db.ClearTransactionDeletesAsync();
        await db.ClearBillOverridesAsync();
        await db.ClearBillDeletesAsync();
        await db.ClearDebtDeletesAsync();
        await db.ClearSavingsGoalDeletesAsync();
        await db.ClearTripDeletesAsync();
        await db.ClearSettingOverridesAsync();
        LastSyncChangeSummary = "Pending phone-side sync intents were cleared. Existing finance data was left alone.";
        await LoadAsync();
    }

    private async Task ReapplyPendingChangesAsync()
    {
        var push = new PushPayload
        {
            NewTransactions = new List<Transaction>(_pendingNewTransactions),
            UpdatedTransactions = new List<Transaction>(_pendingUpdatedTransactions),
            DeletedTransactionIds = new List<int>(_pendingDeletedTransactionIds),
            TransactionEdits = _pendingUpdatedTransactions.Select(ToTransactionEdit).ToList(),
            DeletedTransactions = new List<TransactionDelete>(_pendingDeletedTransactions),
            UpdatedBillStatuses = new List<BillOccurrenceStatus>(_pendingBillStatuses),
            NewBills = new List<Bill>(_pendingNewBills),
            UpdatedBills = new List<Bill>(_pendingUpdatedBills),
            DeletedBillIds = new List<int>(_pendingDeletedBillIds),
            DeletedBills = new List<BillDelete>(_pendingDeletedBills),
            NewDebts = new List<Debt>(_pendingNewDebts),
            UpdatedDebts = new List<Debt>(_pendingUpdatedDebts),
            DeletedDebtIds = new List<int>(_pendingDeletedDebtIds),
            NewDebtPayments = new List<DebtPayment>(_pendingNewDebtPayments),
            DeletedDebtPaymentIds = new List<int>(_pendingDeletedDebtPaymentIds),
            UpdatedAccounts = new List<Account>(_pendingUpdatedAccounts),
            UpdatedSettings = new List<AppSetting>(_pendingUpdatedSettings),
            NewSavingsGoals = new List<SavingsGoal>(_pendingNewSavingsGoals),
            UpdatedSavingsGoals = new List<SavingsGoal>(_pendingUpdatedSavingsGoals),
            DeletedSavingsGoalIds = new List<int>(_pendingDeletedSavingsGoalIds),
            NewTrips = new List<Trip>(_pendingNewTrips),
            UpdatedTrips = new List<Trip>(_pendingUpdatedTrips),
            DeletedTripIds = new List<int>(_pendingDeletedTripIds)
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

        foreach (var edit in push.TransactionEdits)
        {
            var t = FindLocalTransaction(edit);
            if (t is null) continue;
            var updated = new Transaction
            {
                Id = t.Id,
                Date = edit.Date,
                Description = edit.Description,
                AmountCents = edit.AmountCents,
                AccountId = edit.AccountId,
                CategoryId = edit.CategoryId,
                CategoryName = edit.CategoryName,
                TransferId = edit.TransferId,
                UpTransactionId = edit.UpTransactionId,
                IsUnnecessary = edit.IsUnnecessary
            };
            ApplyTransactionEdit(t, updated);
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
        foreach (var deleted in push.DeletedTransactions)
        {
            var t = FindLocalTransaction(deleted);
            if (t is not null && Transactions.Remove(t))
            {
                await db.DeleteAsync("transactions", t.Id);
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

            // Use ps.DueDate (the date the status was originally created) rather than
            // GetOrCreateCurrentStatus, which uses the current effectiveDue.  If WPF
            // advanced bill.DueDate to the next cycle during this sync window,
            // GetOrCreateCurrentStatus would write a paid status for the FUTURE cycle.
            var existing = BillStatuses.FirstOrDefault(s => s.BillId == ps.BillId && s.DueDate.Date == ps.DueDate.Date);
            if (existing is null)
            {
                existing = new BillOccurrenceStatus
                {
                    Id = NextLocalId(BillStatuses.Select(s => s.Id)),
                    BillId = ps.BillId,
                    DueDate = ps.DueDate
                };
                BillStatuses.Add(existing);
            }
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

        // Re-remove phone-deleted bills
        foreach (var id in push.DeletedBillIds)
        {
            if (Bills.RemoveAll(x => x.Id == id) > 0)
            {
                BillStatuses.RemoveAll(s => s.BillId == id);
                await db.DeleteAsync("bills", id);
                changed = true;
            }
        }
        foreach (var deleted in push.DeletedBills)
        {
            var removedIds = Bills
                .Where(b => SameBillDelete(b, deleted))
                .Select(b => b.Id)
                .ToHashSet();
            if (removedIds.Count == 0) continue;

            Bills.RemoveAll(b => removedIds.Contains(b.Id));
            BillStatuses.RemoveAll(s => removedIds.Contains(s.BillId));
            foreach (var removedId in removedIds)
            {
                await db.DeleteAsync("bills", removedId);
            }
            changed = true;
        }

        // Re-add phone-created debts the server doesn't know about yet
        foreach (var d in push.NewDebts)
        {
            if (!Debts.Any(x => x.Id == d.Id))
            {
                Debts.Add(d);
                await db.PutAsync("debts", d);
                changed = true;
            }
        }

        // Re-apply debt edits made on phone
        foreach (var ud in push.UpdatedDebts)
        {
            var debt = Debts.FirstOrDefault(x => x.Id == ud.Id);
            if (debt is null) continue;
            debt.Name = ud.Name;
            debt.BalanceCents = ud.BalanceCents;
            debt.MinimumPaymentCents = ud.MinimumPaymentCents;
            debt.PaymentPeriod = ud.PaymentPeriod;
            debt.InterestRate = ud.InterestRate;
            debt.OriginalBalanceCents = ud.OriginalBalanceCents;
            await db.PutAsync("debts", debt);
            changed = true;
        }

        // Re-remove phone-deleted debts (cascade payments, unlink bills)
        foreach (var id in push.DeletedDebtIds)
        {
            if (Debts.RemoveAll(x => x.Id == id) > 0)
            {
                await db.DeleteAsync("debts", id);
                changed = true;
            }
            foreach (var payment in DebtPayments.Where(p => p.DebtId == id).ToList())
            {
                DebtPayments.Remove(payment);
                await db.DeleteAsync("debtPayments", payment.Id);
                changed = true;
            }
            foreach (var bill in Bills.Where(b => b.DebtId == id))
            {
                bill.DebtId = null;
                await db.PutAsync("bills", bill);
                changed = true;
            }
        }

        // Re-add phone-created debt payments the server doesn't know about yet
        foreach (var p in push.NewDebtPayments)
        {
            var exists = !string.IsNullOrWhiteSpace(p.UpTransactionId)
                ? DebtPayments.Any(x => string.Equals(x.UpTransactionId, p.UpTransactionId, StringComparison.Ordinal))
                : DebtPayments.Any(x => x.Id == p.Id);
            if (!exists)
            {
                DebtPayments.Add(p);
                await db.PutAsync("debtPayments", p);
                changed = true;
            }
        }

        // Re-remove phone-deleted debt payments
        foreach (var id in push.DeletedDebtPaymentIds)
        {
            if (DebtPayments.RemoveAll(x => x.Id == id) > 0)
            {
                await db.DeleteAsync("debtPayments", id);
                changed = true;
            }
        }

        // Re-add phone-created savings goals the server doesn't know about yet
        foreach (var g in push.NewSavingsGoals)
        {
            if (!SavingsGoals.Any(x => x.Id == g.Id))
            {
                SavingsGoals.Add(g);
                await db.PutAsync("savingsGoals", g);
                changed = true;
            }
        }

        // Re-apply savings goal edits made on phone
        foreach (var ug in push.UpdatedSavingsGoals)
        {
            var goal = SavingsGoals.FirstOrDefault(x => x.Id == ug.Id);
            if (goal is null) continue;
            goal.Name = ug.Name;
            goal.TargetCents = ug.TargetCents;
            goal.CurrentCents = ug.CurrentCents;
            goal.WeeklyContributionCents = ug.WeeklyContributionCents;
            goal.TargetDate = ug.TargetDate;
            await db.PutAsync("savingsGoals", goal);
            changed = true;
        }

        // Re-remove phone-deleted savings goals
        foreach (var id in push.DeletedSavingsGoalIds)
        {
            if (SavingsGoals.RemoveAll(x => x.Id == id) > 0)
            {
                await db.DeleteAsync("savingsGoals", id);
                changed = true;
            }
        }

        // Re-add phone-created trips the server doesn't know about yet
        foreach (var t in push.NewTrips)
        {
            if (!Trips.Any(x => x.Id == t.Id))
            {
                Trips.Add(t);
                await db.PutAsync("trips", t);
                changed = true;
            }
        }

        // Re-apply trip edits made on phone
        foreach (var ut in push.UpdatedTrips)
        {
            var trip = Trips.FirstOrDefault(x => x.Id == ut.Id);
            if (trip is null) continue;
            trip.Name = ut.Name;
            trip.Destination = ut.Destination;
            trip.Notes = ut.Notes;
            trip.StartDate = ut.StartDate;
            trip.EndDate = ut.EndDate;
            trip.SavingsAccountId = ut.SavingsAccountId;
            trip.WeeklyContributionCents = ut.WeeklyContributionCents;
            trip.Itinerary = ut.Itinerary;
            trip.Checklist = ut.Checklist;
            trip.BudgetItems = ut.BudgetItems;
            await db.PutAsync("trips", trip);
            changed = true;
        }

        // Re-remove phone-deleted trips
        foreach (var id in push.DeletedTripIds)
        {
            if (Trips.RemoveAll(x => x.Id == id) > 0)
            {
                await db.DeleteAsync("trips", id);
                changed = true;
            }
        }

        // Re-apply account goal changes made on phone
        foreach (var ua in push.UpdatedAccounts)
        {
            var account = Accounts.FirstOrDefault(x => x.Id == ua.Id);
            if (account is null) continue;
            account.TargetCents = ua.TargetCents;
            account.TargetDate = ua.TargetDate;
            account.TargetStartDate = ua.TargetStartDate;
            account.TargetStartingBalanceCents = ua.TargetStartingBalanceCents;
            await db.PutAsync("accounts", account);
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

    private Transaction? FindLocalTransaction(TransactionEdit edit)
    {
        if (!string.IsNullOrWhiteSpace(edit.UpTransactionId))
        {
            var byUpId = Transactions.FirstOrDefault(t =>
                string.Equals(t.UpTransactionId, edit.UpTransactionId, StringComparison.Ordinal));
            if (byUpId is not null) return byUpId;
        }

        return Transactions.FirstOrDefault(t => t.Id == edit.Id) ??
            Transactions.FirstOrDefault(t => SameTransactionSignature(t.Date, t.Description, t.AmountCents, edit.Date, edit.Description, edit.AmountCents));
    }

    private Transaction? FindLocalTransaction(TransactionDelete deleted)
    {
        if (!string.IsNullOrWhiteSpace(deleted.UpTransactionId))
        {
            var byUpId = Transactions.FirstOrDefault(t =>
                string.Equals(t.UpTransactionId, deleted.UpTransactionId, StringComparison.Ordinal));
            if (byUpId is not null) return byUpId;
        }

        return Transactions.FirstOrDefault(t => t.Id == deleted.Id) ??
            Transactions.FirstOrDefault(t => SameTransactionSignature(t.Date, t.Description, t.AmountCents, deleted.Date, deleted.Description, deleted.AmountCents));
    }

    private static bool SameTransactionSignature(Transaction left, Transaction right) =>
        SameTransactionSignature(left.Date, left.Description, left.AmountCents, right.Date, right.Description, right.AmountCents);

    private static bool SameTransactionSignature(DateTime leftDate, string leftDescription, int leftAmountCents, DateTime rightDate, string rightDescription, int rightAmountCents)
    {
        if (leftAmountCents != rightAmountCents) return false;
        if (!string.Equals(NormalizeDescription(leftDescription), NormalizeDescription(rightDescription), StringComparison.OrdinalIgnoreCase)) return false;
        return Math.Abs((leftDate.Date - rightDate.Date).TotalDays) <= 3;
    }

    private static string NormalizeDescription(string? description) => (description ?? string.Empty).Trim();
}
