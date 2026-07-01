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
    // Phone-only: self-imposed spending limits (never synced, never real money)
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
    public decimal BudgetSafeToSpendAmount => Math.Max(BudgetLeftover, 0);
    // Reactive, pace-aware version of the safe-to-spend headline: starts from
    // what's actually left in the current pay cycle (allowance minus real
    // discretionary spend so far) rather than the flat weekly plan figure, then
    // tightens further if today's spending rate projects you to run out before
    // the cycle ends. BudgetSafeToSpendAmount stays untouched as the static plan
    // number other features (challenges, the budget editor) key off.
    public decimal SafeToSpendAmount
    {
        get
        {
            if (NoSpendMode) return 0m;

            var (from, to) = GetCurrentPeriod();
            var periodDays = Math.Max((to.Date - from.Date).Days + 1, 1);
            var periodBudget = Math.Max(BudgetLeftover * (periodDays / 7m), 0m);

            var today = DateTime.Today.Date < from.Date ? from.Date
                : DateTime.Today.Date > to.Date ? to.Date
                : DateTime.Today.Date;
            var elapsedDays = Math.Clamp((today - from.Date).Days + 1, 1, periodDays);
            var spendToDate = GetDiscretionarySpendingForPeriod(from, today);
            var remaining = periodBudget - spendToDate;
            if (remaining <= 0) return 0m;

            var projectedOverrun = (spendToDate / elapsedDays) * periodDays - periodBudget;
            return projectedOverrun > 0 ? Math.Max(remaining - projectedOverrun, 0m) : remaining;
        }
    }
    public decimal OutstandingLentDollars =>
        Transactions
            .Where(t => IsUnrepaid(t.Id))
            .Sum(GetLentOutstandingDollars);
    public decimal SafeToSpendIfReimbursed => SafeToSpendAmount + OutstandingLentDollars;

    // ── Settings ──────────────────────────────────────────────────────────────
    public DateTime NextPayDate { get; private set; } = DateTime.Today;
    public int DaysUntilPayday => Math.Max((NextPayDate.Date - DateTime.Today).Days, 0);
    public string SummaryPeriod { get; private set; } = "Weekly";
    public bool NoSpendMode { get; private set; }
    public DateTime? NoSpendModeSince { get; private set; }

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
    private const string PaceExcludedCategoriesSettingKey = "SpendingPaceExcludedCategories";
    private const string PaceExcludedTransactionNamesSettingKey = "SpendingPaceExcludedTransactionNames";
    private const string CategoryManagementRulesSettingKey = "CategoryManagementRules";
    private const string TransactionCategoryRulesSettingKey = "TransactionCategoryRules";
    private HashSet<string> _paceExcludedCategories = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _paceExcludedTransactionNames = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> PaceExcludedCategories => _paceExcludedCategories;
    public IReadOnlyCollection<string> PaceExcludedTransactionNames => _paceExcludedTransactionNames;
    private CategoryManagementRules _categoryManagementRules = new();
    private TransactionCategoryRules _transactionCategoryRules = new();

    public sealed class CategoryManagementRules
    {
        public List<ManagedCategoryRule> AddedCategories { get; set; } = new();
        public List<DeletedCategoryRule> DeletedCategories { get; set; } = new();
    }

    public sealed class ManagedCategoryRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public CategoryType Type { get; set; } = CategoryType.Expense;
    }

    public sealed class DeletedCategoryRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public CategoryType Type { get; set; } = CategoryType.Expense;
        public int ReplacementId { get; set; }
        public string ReplacementName { get; set; } = string.Empty;
    }

    public sealed class TransactionCategoryRules
    {
        public List<TransactionCategoryRule> Rules { get; set; } = new();
    }

    public sealed class TransactionCategoryRule
    {
        public string NormalizedName { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
    }

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
    public bool IsSyncing => _syncInProgress || sync.IsSyncing;

    // The PC desktop app only reconciles phone_push into its own database on its
    // own ~5-minute timer, then re-pushes its (now-stale, pre-reconciliation)
    // snapshot to the shared finance_sync cloud row. If a pull lands in that
    // window, it can overwrite an edit the phone already successfully pushed
    // moments earlier — the edit reappears, then silently reverts on the next
    // sync even though nothing _pending remains to defend it. Keep replaying the
    // last confirmed push for a grace period that comfortably outlasts that
    // window, regardless of whether the pending queues have since drained.
    private PushPayload? _lastConfirmedPush;
    private DateTime _lastConfirmedPushAt;
    private static readonly TimeSpan ConfirmedPushGrace = TimeSpan.FromMinutes(6);

    // LoadAsync() wholesale-replaces each Trip object from IndexedDB, then
    // ReapplyPushChangesAsync/ReapplyPendingChangesAsync correct it back up a
    // moment later. A Trip edit (Add/Update/Delete) that lands in that gap reads
    // the not-yet-corrected object, clones its *whole* nested Budget/Itinerary/
    // Checklist collections (see CloneTrip) into the pending queue, and that
    // stale clone then gets replayed last — silently reverting a sibling edit
    // that had already synced. Gate Trip mutators against the same window so an
    // edit always sees a fully-settled Trips collection, never a mid-reapply one.
    private readonly SemaphoreSlim _tripMutationGate = new(1, 1);

    // Diagnostic trail for the trip-item-reverting bug: records the affected
    // trip's itinerary at every mutation/load/reapply step so a repro can be
    // traced after the fact instead of guessed at. Surfaced read-only on the
    // Tools page. Capped so it can't grow unbounded across a long session.
    public List<string> TripDebugLog { get; } = new();
    private void LogTrip(string msg)
    {
        TripDebugLog.Add($"{DateTime.Now:HH:mm:ss.fff} {msg}");
        if (TripDebugLog.Count > 300) TripDebugLog.RemoveAt(0);
    }
    private static string ItinSnapshot(Trip? t)
    {
        if (t is null) return "null";
        var itin = string.Join(",", t.Itinerary.Select(i => $"{i.Title}={i.AmountDollars:0.00}#{(i.Id.Length >= 6 ? i.Id[..6] : i.Id)}"));
        var check = string.Join(",", t.Checklist.Select(c => $"{c.Text}:{(c.Done ? "Y" : "N")}#{(c.Id.Length >= 6 ? c.Id[..6] : c.Id)}"));
        var budget = string.Join(",", t.BudgetItems.Select(b => $"{b.Category}:{(b.Paid ? "Y" : "N")}={b.ActualCents}#{(b.Id.Length >= 6 ? b.Id[..6] : b.Id)}"));
        return $"itin=[{itin}] checklist=[{check}] budget=[{budget}]";
    }

    // Diagnostic trail for the savings-goal delete-then-reappear bug: records
    // every load, delete, and tombstone-defense decision so a repro can be
    // traced after the fact instead of guessed at. Surfaced read-only on the
    // Tools page. Capped so it can't grow unbounded across a long session.
    public List<string> SavingsGoalDebugLog { get; } = new();
    private void LogGoal(string msg)
    {
        SavingsGoalDebugLog.Add($"{DateTime.Now:HH:mm:ss.fff} {msg}");
        if (SavingsGoalDebugLog.Count > 300) SavingsGoalDebugLog.RemoveAt(0);
    }
    private static string GoalSnapshot(SavingsGoal g) =>
        $"id={g.Id} name={g.Name} group={g.GroupName ?? "(none)"} target={g.TargetCents} current={g.CurrentCents}";

    // Called by MainLayout on every app-visible event so a sync interrupted by
    // iOS suspension doesn't permanently block the guard.
    public void ForceResetSyncGuard() => _syncInProgress = false;

    public string LastSyncChangeSummary { get; private set; } = "No sync changes summarized yet.";
    public string? LastSyncPushStatus { get; private set; }

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

    public List<SyncQueueItem> GetPendingSyncQueue()
    {
        var items = new List<SyncQueueItem>();
        AddQueueItem(items, "New transactions", _pendingNewTransactions.Count, "Transactions");
        AddQueueItem(items, "Edited transactions", _pendingUpdatedTransactions.Count, "Transactions");
        AddQueueItem(items, "Deleted transactions", Math.Max(_pendingDeletedTransactionIds.Count, _pendingDeletedTransactions.Count), "Transactions");
        AddQueueItem(items, "Bill paid/unpaid changes", _pendingBillStatuses.Count, "Bills");
        AddQueueItem(items, "New bills", _pendingNewBills.Count, "Bills");
        AddQueueItem(items, "Edited bills", _pendingUpdatedBills.Count, "Bills");
        AddQueueItem(items, "Deleted bills", Math.Max(_pendingDeletedBillIds.Count, _pendingDeletedBills.Count), "Bills");
        AddQueueItem(items, "Budget/settings changes", _pendingUpdatedSettings.Count, "Settings");
        AddQueueItem(items, "Account changes", _pendingUpdatedAccounts.Count, "Accounts");
        AddQueueItem(items, "Debt changes", _pendingNewDebts.Count + _pendingUpdatedDebts.Count + _pendingDeletedDebtIds.Count + _pendingNewDebtPayments.Count + _pendingDeletedDebtPaymentIds.Count, "Debts");
        AddQueueItem(items, "Savings goal changes", _pendingNewSavingsGoals.Count + _pendingUpdatedSavingsGoals.Count + _pendingDeletedSavingsGoalIds.Count, "Savings");
        AddQueueItem(items, "Trip changes", _pendingNewTrips.Count + _pendingUpdatedTrips.Count + _pendingDeletedTripIds.Count, "Trips");
        return items;
    }

    private static void AddQueueItem(List<SyncQueueItem> items, string label, int count, string kind)
    {
        if (count <= 0) return;
        items.Add(new SyncQueueItem { Label = label, Count = count, Kind = kind });
    }

    public async Task<(int Edits, int Deletes)> GetPersistedTransactionIntentCountsAsync()
    {
        var edits = await db.GetPendingTransactionOverridesAsync();
        var deletes = await db.GetPendingTransactionDeletesAsync();
        return (edits.Count, deletes.Count);
    }

    public event Action? OnChange;

    // Fired when a not-yet-synced (negative-Id) Trip is matched to the real
    // Id the server assigned it, so UI holding the old Id (e.g. a selected-
    // trip detail view) can follow the rename instead of losing its place.
    public event Action<int, int>? OnTripIdAdopted;
    private readonly Dictionary<int, int> _adoptedTripIds = new();

    // ── Account balances computed from transactions ───────────────────────────
    public Dictionary<int, decimal> AccountBalances { get; private set; } = new();

    // ── Bills for current period ──────────────────────────────────────────────
    public List<Bill> BillsDueBeforePayday { get; private set; } = new();
    public decimal TotalBillsDue { get; private set; }
    public List<BillAccountShortfall> BillAccountShortfalls { get; private set; } = new();
    public decimal TotalBillShortfall => BillAccountShortfalls.Sum(s => s.Needed);

    // ── Gamification state ──────────────────────────────────────────────────────
    public StreakState Streak { get; private set; } = new();
    public XpState Xp { get; private set; } = new();
    public List<WeeklyChallengeState> WeeklyChallenges { get; private set; } = new();
    public RoundUpState RoundUp { get; private set; } = new();
    public BadgeState Badges { get; private set; } = new();
    public List<BadgeDefinition> UnlockedBadges =>
        BadgeCatalog.All.Where(b => Badges.UnlockedBadgeIds.Contains(b.Id)).ToList();
    public List<BadgeDefinition> LockedBadges =>
        BadgeCatalog.All.Where(b => !Badges.UnlockedBadgeIds.Contains(b.Id)).ToList();
    public decimal GetWeeklyChallengeProgress(WeeklyChallengeState challenge) =>
        string.IsNullOrEmpty(challenge.CategoryName)
            ? GetWeekSpending(0)
            : Transactions
                .Where(t => t.Date.Date >= GetIsoWeekStart(DateTime.Today)
                    && t.AmountCents < 0
                    && string.Equals(t.CategoryName, challenge.CategoryName, StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.AmountDollars));

    // ── Transactions for display (most recent 100) ────────────────────────────
    public List<Transaction> RecentTransactions { get; private set; } = new();
    public List<ProactiveInsight> ProactiveInsights { get; private set; } = new();
    // Superset of ProactiveInsights that also folds in the Tools-page-only suggestion
    // sources (cleanup/watchlist/bill intelligence), so Dashboard, push notifications,
    // and the Tools snapshot all agree on "what needs attention" instead of each
    // reading a different subset.
    public List<ProactiveInsight> SmartSignals { get; private set; } = new();

    public bool IsLoaded { get; private set; }
    public bool HasAnyFinanceData =>
        Accounts.Count > 0 ||
        Transactions.Count > 0 ||
        Bills.Count > 0 ||
        Debts.Count > 0 ||
        SavingsGoals.Count > 0 ||
        WeeklyBudgets.Count > 0 ||
        Trips.Count > 0;

    // Suppresses LoadAsync's own OnChange while SyncAndReloadAsync is mid
    // replace-then-correct: LoadAsync() wholesale-replaces Trips from IndexedDB
    // (which may briefly be stale, pending the reapply that runs right after),
    // so firing OnChange here gives subscribers like HolidayTab a render of the
    // not-yet-corrected snapshot — visible to the user as a value reverting for
    // about a second before silently fixing itself. SyncAndReloadAsync fires its
    // own OnChange once the reapply is done, so nothing is lost by skipping this one.
    private bool _suppressLoadOnChange;

    // Suppresses ApplyPersisted* re-seeding inside LoadStoresAsync when called
    // from SyncAndReloadAsync. Those methods are designed for app-restart
    // restoration: they read IndexedDB overrides and unconditionally push them
    // back onto the in-memory _pending* lists. During a live sync the _pending*
    // lists already reflect the correct intent (cleared after a successful push,
    // or still populated if the push failed), so re-seeding from IndexedDB would
    // either create phantom pending changes (when ClearDebtOverrideAsync fails
    // silently after a successful push, or when HasPendingChanges was false but a
    // stale override remained in IndexedDB) or double-queue the same change.
    // ReapplyPendingChangesAsync() runs immediately after LoadAsync() inside
    // SyncAndReloadAsync and reapplies whatever is legitimately in the lists.
    private bool _suppressPendingReseed;

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
        if (!_suppressLoadOnChange) OnChange?.Invoke();
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
        foreach (var g in SavingsGoals) LogGoal($"LOAD {GoalSnapshot(g)}");
        WeeklyBudgets = await db.GetWeeklyBudgetsAsync();
        AppSettings = await db.GetAppSettingsAsync();
        Trips = await db.GetTripsAsync();
        foreach (var t in Trips) LogTrip($"LOAD trip={t.Id} {ItinSnapshot(t)}");
        LentTransactions = await db.GetLentTransactionsAsync();
        NormaliseLentRepayments();
        await RemoveInvalidLentTransactionsAsync();
        _unrepaidLentIds = LentTransactions.Where(IsLentOutstanding).Select(l => l.Id).ToHashSet();

        // Apply any phone-side overrides that survived a cloud replace or app restart.
        // Skipped during mid-session sync reloads (_suppressPendingReseed) because the
        // in-memory _pending* lists already carry the correct intent and
        // ReapplyPendingChangesAsync() runs right after to reapply them — re-seeding
        // from IndexedDB here would create phantom pending changes.
        if (!_suppressPendingReseed)
        {
            await ApplyPersistedTransactionOverridesAsync();
            await ApplyPersistedTransactionDeletesAsync();
            await ApplyPersistedBillDeletesAsync();
            await ApplyPersistedDebtDeletesAsync();
            await ApplyPersistedSavingsGoalDeletesAsync();
            await ApplyPersistedTripDeletesAsync();
            await ApplyPersistedTripOverridesAsync();
            await ApplyPersistedBillOverridesAsync();
            await ApplyPersistedBillEditOverridesAsync();
            await ApplyPersistedDebtOverridesAsync();
            await ApplyPersistedAccountOverridesAsync();
            await ApplyPersistedSavingsGoalOverridesAsync();
            await ApplyPersistedSettingOverridesAsync();
        }
        await ApplyManagedCategoryRulesAsync(persistTransactionOverrides: false);
        await ApplyTransactionCategoryRulesAsync(persistTransactionOverrides: false);
    }

    // ── Lent money tracking ──────────────────────────────────────────────────
    // Internal cover/envelope transfers can never be "lent out" — guard against
    // a stale LentTransaction record (e.g. left over from a transaction Id that
    // got reused by a later sync) silently re-attaching to one of these.
    public bool IsLent(int txnId) =>
        LentTransactions.Any(l => l.Id == txnId) && IsLentEligibleById(txnId);
    public bool IsUnrepaid(int txnId) =>
        _unrepaidLentIds.Contains(txnId) && IsLentEligibleById(txnId);

    private bool IsLentEligibleById(int txnId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == txnId);
        return t is not null && IsLentEligibleTransaction(t);
    }

    // Deliberately narrower than IsBudgetedBillTransaction: a transaction's
    // category alone (e.g. "Debt", "Rent") shouldn't block marking it as lent
    // out — people categorise IOUs to friends under whatever category fits.
    // Only block actual matched recurring bills/known committed payments,
    // same as the internal-movement guard above.
    private bool IsLentEligibleTransaction(Transaction t) =>
        t.AmountCents < 0 &&
        !IsInternalMovement(t) &&
        !MatchesBillRecord(t) &&
        !MatchesKnownBudgetedPayment(t);

    public decimal GetLentRepaidDollars(int txnId) =>
        IsLentEligibleById(txnId)
            ? LentTransactions.FirstOrDefault(l => l.Id == txnId)?.RepaidDollars ?? 0m
            : 0m;

    public decimal GetLentOutstandingDollars(Transaction transaction)
    {
        if (!IsLentEligibleTransaction(transaction)) return 0m;
        var lent = LentTransactions.FirstOrDefault(l => l.Id == transaction.Id);
        if (lent is null) return 0m;
        return Math.Max(Math.Abs(transaction.AmountDollars) - lent.RepaidDollars, 0m);
    }

    public async Task MarkLentAsync(int txnId, string note)
    {
        if (!IsLentEligibleById(txnId)) return;
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
        if (transaction is null || !IsLentEligibleTransaction(transaction)) return;
        lent.RepaidCents = Math.Abs(transaction.AmountCents);
        lent.Repaid = true;
        _unrepaidLentIds.Remove(txnId);
        await db.SetLentTransactionAsync(lent);
        OnChange?.Invoke();
    }

    public async Task RecordLentRepaymentAsync(int txnId, decimal amountDollars)
    {
        var lent = LentTransactions.FirstOrDefault(l => l.Id == txnId);
        var transaction = Transactions.FirstOrDefault(t => t.Id == txnId);
        if (lent is null || transaction is null || !IsLentEligibleTransaction(transaction) || amountDollars <= 0) return;

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
            if (!IsLentEligibleTransaction(transaction)) continue;
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

    private async Task RemoveInvalidLentTransactionsAsync()
    {
        var invalid = LentTransactions
            .Where(l => !IsLentEligibleById(l.Id))
            .Select(l => l.Id)
            .Distinct()
            .ToList();

        foreach (var id in invalid)
        {
            LentTransactions.RemoveAll(l => l.Id == id);
            _unrepaidLentIds.Remove(id);
            await db.DeleteLentTransactionAsync(id);
        }
    }

    private bool IsLentOutstanding(LentTransaction lent)
    {
        var transaction = Transactions.FirstOrDefault(t => t.Id == lent.Id);
        if (transaction is null || !IsLentEligibleTransaction(transaction)) return false;
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

    private async Task ApplyPersistedBillEditOverridesAsync()
    {
        var overrides = await db.GetPendingBillEditOverridesAsync();
        foreach (var ov in overrides)
        {
            var bill = ov.Bill;
            var existing = Bills.FirstOrDefault(b => b.Id == bill.Id)
                ?? Bills.FirstOrDefault(b => SameBillSnapshot(b, bill));
            if (existing is null)
            {
                Bills.Add(bill);
                await db.PutAsync("bills", bill);
                if (bill.Id < 0 && !_pendingNewBills.Any(b => b.Id == bill.Id))
                    _pendingNewBills.Add(bill);
                continue;
            }

            CopyBillFields(bill, existing);
            existing.AccountName = Accounts.FirstOrDefault(a => a.Id == existing.AccountId)?.Name ?? existing.AccountName;
            await db.PutAsync("bills", existing);
            if (bill.Id != existing.Id)
            {
                await db.ClearBillEditOverrideAsync(bill.Id);
                await db.SetBillEditOverrideAsync(existing);
            }

            // Re-queue for push — a bill edit made locally but never confirmed
            // pushed (e.g. iOS killed the app mid-sync) must survive an app
            // restart and a stale pull, same as transaction/setting overrides.
            if (existing.Id < 0)
            {
                if (!_pendingNewBills.Any(b => b.Id == existing.Id))
                    _pendingNewBills.Add(existing);
            }
            else
            {
                _pendingUpdatedBills.RemoveAll(b => b.Id == existing.Id);
                _pendingUpdatedBills.Add(CloneBill(existing));
            }
        }
    }

    private async Task ApplyPersistedDebtOverridesAsync()
    {
        var overrides = await db.GetPendingDebtOverridesAsync();
        foreach (var ov in overrides)
        {
            var debt = ov.Debt;
            var existing = Debts.FirstOrDefault(d => d.Id == debt.Id)
                ?? Debts.FirstOrDefault(d => SameDebtSnapshot(d, debt));
            if (existing is null)
            {
                Debts.Add(debt);
                await db.PutAsync("debts", debt);
                if (debt.Id < 0 && !_pendingNewDebts.Any(d => d.Id == debt.Id))
                    _pendingNewDebts.Add(debt);
                continue;
            }

            CopyDebtFields(debt, existing);
            await db.PutAsync("debts", existing);
            if (debt.Id != existing.Id)
            {
                await db.ClearDebtOverrideAsync(debt.Id);
                await db.SetDebtOverrideAsync(existing);
            }

            if (existing.Id < 0)
            {
                if (!_pendingNewDebts.Any(d => d.Id == existing.Id))
                    _pendingNewDebts.Add(existing);
            }
            else
            {
                _pendingUpdatedDebts.RemoveAll(d => d.Id == existing.Id);
                _pendingUpdatedDebts.Add(CloneDebt(existing));
            }
        }
    }

    private async Task ApplyPersistedAccountOverridesAsync()
    {
        var overrides = await db.GetPendingAccountOverridesAsync();
        foreach (var ov in overrides)
        {
            var account = ov.Account;
            var existing = Accounts.FirstOrDefault(a => a.Id == account.Id);
            if (existing is null) continue;
            existing.TargetCents = account.TargetCents;
            existing.TargetDate = account.TargetDate;
            existing.TargetStartDate = account.TargetStartDate;
            existing.TargetStartingBalanceCents = account.TargetStartingBalanceCents;
            await db.PutAsync("accounts", existing);

            _pendingUpdatedAccounts.RemoveAll(a => a.Id == existing.Id);
            _pendingUpdatedAccounts.Add(CloneAccount(existing));
        }
    }

    private async Task ApplyPersistedSavingsGoalOverridesAsync()
    {
        var overrides = await db.GetPendingSavingsGoalOverridesAsync();
        foreach (var ov in overrides)
        {
            var goal = ov.Goal;
            var existing = SavingsGoals.FirstOrDefault(g => g.Id == goal.Id)
                ?? SavingsGoals.FirstOrDefault(g => SameSavingsGoalSnapshot(g, goal));
            if (existing is null)
            {
                SavingsGoals.Add(goal);
                await db.PutAsync("savingsGoals", goal);
                if (goal.Id < 0 && !_pendingNewSavingsGoals.Any(g => g.Id == goal.Id))
                    _pendingNewSavingsGoals.Add(goal);
                continue;
            }

            CopySavingsGoalFields(goal, existing);
            await db.PutAsync("savingsGoals", existing);
            if (goal.Id != existing.Id)
            {
                await db.ClearSavingsGoalOverrideAsync(goal.Id);
                await db.SetSavingsGoalOverrideAsync(existing);
            }

            if (existing.Id < 0)
            {
                if (!_pendingNewSavingsGoals.Any(g => g.Id == existing.Id))
                    _pendingNewSavingsGoals.Add(existing);
            }
            else
            {
                _pendingUpdatedSavingsGoals.RemoveAll(g => g.Id == existing.Id);
                _pendingUpdatedSavingsGoals.Add(CloneSavingsGoal(existing));
            }
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
            if (ShouldSyncSetting(setting.Key))
            {
                _pendingUpdatedSettings.Add(setting);
            }
            else
            {
                await db.ClearSettingOverrideAsync(setting.Key);
            }
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
                await db.ClearBillEditOverrideAsync(id);
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
                await db.ClearDebtOverrideAsync(id);
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
            LogGoal($"TOMBSTONE id={deleted.Id} name={deleted.Name} group={deleted.GroupName ?? "(none)"} target={deleted.TargetCents} current={deleted.CurrentCents} matched=[{string.Join(",", removedIds)}]");
            if (removedIds.Count == 0)
            {
                // Nothing in this freshly-pulled snapshot matches anymore — the
                // server has actually reconciled the delete now. Only stop
                // defending it once that's confirmed, not just because a push
                // succeeded (see the comment in SyncAndReloadAsync).
                await db.ClearSavingsGoalDeleteAsync(deleted.Id);
                continue;
            }

            SavingsGoals.RemoveAll(g => removedIds.Contains(g.Id));
            _pendingNewSavingsGoals.RemoveAll(g => removedIds.Contains(g.Id));
            _pendingUpdatedSavingsGoals.RemoveAll(g => removedIds.Contains(g.Id));
            foreach (var id in removedIds.Where(id => id > 0))
            {
                _pendingDeletedSavingsGoalIds.RemoveAll(x => x == id);
                _pendingDeletedSavingsGoalIds.Add(id);
                // Without this, IndexedDB still has the old record (it was only
                // matched here by content, not by the id the original delete
                // persisted), so the very next LoadStoresAsync() call reads it
                // straight back out of the DB and the goal looks "revived".
                await db.DeleteAsync("savingsGoals", id);
                await db.ClearSavingsGoalOverrideAsync(id);
            }
        }
    }

    private async Task ApplyPersistedTripOverridesAsync()
    {
        var overrides = await db.GetPendingTripOverridesAsync();
        foreach (var ov in overrides)
        {
            var updated = ov.Trip;
            var trip = Trips.FirstOrDefault(t => t.Id == updated.Id)
                ?? Trips.FirstOrDefault(t => SameTripAdoptionCandidate(t, updated));
            if (trip is null)
            {
                await db.ClearTripOverrideAsync(updated.Id);
                continue;
            }

            var oldId = updated.Id;
            if (oldId != trip.Id)
                AdoptTripId(oldId, trip.Id);

            CopyTripFields(updated, trip);
            LogTrip($"OVERRIDE-APPLY trip={trip.Id} {ItinSnapshot(trip)}");
            await db.PutAsync("trips", trip);
            if (oldId != trip.Id)
            {
                await db.ClearTripOverrideAsync(oldId);
                await db.SetTripOverrideAsync(CloneTrip(trip));
            }

            // Re-queue for push — a trip edit made locally but never confirmed
            // pushed (e.g. iOS killed the app mid-sync) must survive an app
            // restart and a stale pull, same as transaction/setting overrides.
            // Route by id sign: a still-unadopted trip was never seen by the
            // server, so it must go out as NewTrips, not UpdatedTrips, or the
            // server-side merge has nothing matching to update.
            if (trip.Id > 0)
            {
                _pendingUpdatedTrips.RemoveAll(t => t.Id == trip.Id);
                _pendingUpdatedTrips.Add(CloneTrip(trip));
            }
            else
            {
                _pendingNewTrips.RemoveAll(t => t.Id == trip.Id);
                _pendingNewTrips.Add(CloneTrip(trip));
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
        // Insight computation is supplementary — it must never be able to stop the rest
        // of Compute() (and the UI re-render/sync that follows it) from completing.
        try { ComputeProactiveInsights(); } catch { }
        try { ComputeStreaks(); } catch { }
        try { ComputeXp(); } catch { }
        try { ComputeWeeklyChallenge(); } catch { }
        try { ComputeRoundUp(); } catch { }
        try { ComputeBadges(); } catch { }
        try { ComputeUnifiedSmartSignals(); } catch { }
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

        SummaryPeriod = GetSetting("SummaryPeriod") ?? "Weekly";
        NoSpendMode = string.Equals(GetSetting("NoSpendMode"), "true", StringComparison.OrdinalIgnoreCase);
        NoSpendModeSince = DateTime.TryParse(GetSetting("NoSpendModeSince"), out var noSpendSince) ? noSpendSince : null;

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
        _paceExcludedCategories = BuildPaceExclusionSet(GetSettingJson<List<string>>(PaceExcludedCategoriesSettingKey));
        _paceExcludedTransactionNames = BuildPaceExclusionSet(GetSettingJson<List<string>>(PaceExcludedTransactionNamesSettingKey));

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

        // Phone-side overrides take precedence over the desktop-synced WeeklyBudget
        // record, so targets can be tweaked from the phone without WPF running.
        WeeklyIncome = GetBudgetOverride("Income", WeeklyIncome);
        BudgetBills = GetBudgetOverride("Bills", BudgetBills);
        BudgetEssentials = GetBudgetOverride("Essentials", BudgetEssentials);
        BudgetSavings = GetBudgetOverride("Savings", BudgetSavings);
        BudgetUnplanned = GetBudgetOverride("Unplanned", BudgetUnplanned);

        PlannedSavingsTransfers = CalculatePlannedSavingsTransfers();
        BudgetSavings = Math.Max(BudgetSavings, PlannedSavingsTransfers);
    }

    private decimal GetBudgetOverride(string field, decimal fallback)
    {
        var raw = GetSetting($"WeeklyBudgetOverride:{field}");
        return decimal.TryParse(raw, out var value) ? value : fallback;
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

    public sealed record HolidayFundingOption(
        int AccountId,
        string AccountName,
        decimal CurrentBalance,
        decimal TargetBalance,
        decimal ReservedBills,
        decimal SpareDollars,
        DateTime ProtectedUntil,
        bool HasTarget);

    public List<HolidayFundingOption> GetHolidayFundingOptions(Trip trip)
    {
        var protectedUntil = trip.StartDate?.Date ?? DateTime.Today.AddDays(30);

        return Accounts
            .Where(a => a.Type != AccountType.Credit)
            .Where(a => a.Id != PayAccountId)
            .Where(a => trip.SavingsAccountId is null || a.Id != trip.SavingsAccountId.Value)
            .Where(a => a.Type is AccountType.Savings or AccountType.Cash || a.TargetDollars is > 0)
            .Select(account =>
            {
                var balance = GetAccountBalance(account.Id);
                var target = account.TargetDollars ?? 0m;
                var reservedBills = Bills
                    .Where(b => b.AccountId == account.Id &&
                                !IsBillPaid(b) &&
                                b.EffectiveDueDate.Date <= protectedUntil)
                    .Sum(b => b.AmountDollars);
                var spare = Math.Max(balance - reservedBills - target, 0m);
                return new HolidayFundingOption(
                    account.Id,
                    account.Name,
                    balance,
                    target,
                    reservedBills,
                    Math.Round(spare, 2),
                    protectedUntil,
                    account.TargetDollars is > 0);
            })
            .Where(o => o.SpareDollars > 0)
            .OrderByDescending(o => o.SpareDollars)
            .ToList();
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

    // A recurring Bill only ever tracks its single current/pending occurrence
    // (EffectiveDueDate) — there's no separate record for "next week's" instance
    // until the current one is settled. Forward-looking views (Bills' Due Soon,
    // Budget's Forecast) need to see several cycles ahead, so this projects
    // multiple future occurrences per bill instead of just the current one.
    public List<BillOccurrencePreview> GetUpcomingBillOccurrences(DateTime from, DateTime to)
    {
        var result = new List<BillOccurrencePreview>();
        foreach (var bill in Bills)
        {
            var due = bill.EffectiveDueDate == default ? bill.DueDate.Date : bill.EffectiveDueDate.Date;
            while (due < from.Date)
                due = AdvanceDueDate(due, bill.Frequency);

            while (due <= to.Date)
            {
                if (!IsBillOccurrencePaid(bill, due))
                    result.Add(new BillOccurrencePreview(bill, due));
                due = AdvanceDueDate(due, bill.Frequency);
            }
        }
        return result.OrderBy(o => o.DueDate).ThenBy(o => o.Bill.Name).ToList();
    }

    public bool IsBillOccurrencePaid(Bill bill, DateTime dueDate)
    {
        var exact = BillStatuses.FirstOrDefault(s => s.BillId == bill.Id && s.DueDate.Date == dueDate.Date);
        if (exact is not null) return exact.IsPaid;
        var effective = bill.EffectiveDueDate == default ? bill.DueDate.Date : bill.EffectiveDueDate.Date;
        return dueDate.Date == effective.Date && IsBillPaid(bill);
    }

    // GetEffectiveDueDate only ever surfaces ONE date per bill — the current/next
    // cycle — so a bill left unpaid for a couple of cycles silently loses its
    // earlier occurrences: they're never shown as a row and can never be marked
    // paid individually. This instead walks forward from the bill's own anchor
    // date (bounded by lookbackDays so old/stale bills don't generate years of
    // phantom rows) up to and including the current cycle, returning every
    // occurrence in between that has no paid status — so missed weeks show up
    // as their own rows instead of being skipped over.
    public List<BillOccurrencePreview> GetOutstandingBillOccurrences(int lookbackDays = 60)
    {
        var result = new List<BillOccurrencePreview>();
        var earliest = DateTime.Today.AddDays(-lookbackDays);
        foreach (var bill in Bills)
        {
            var due = bill.DueDate.Date;
            while (due < earliest)
                due = AdvanceDueDate(due, bill.Frequency);

            var current = bill.EffectiveDueDate == default ? GetEffectiveDueDate(bill) : bill.EffectiveDueDate;
            while (due <= current.Date)
            {
                if (!IsBillOccurrencePaid(bill, due))
                    result.Add(new BillOccurrencePreview(bill, due));
                due = AdvanceDueDate(due, bill.Frequency);
            }
        }
        return result.OrderBy(o => o.DueDate).ThenBy(o => o.Bill.Name).ToList();
    }

    // The Paid tab needs each bill's actual paid occurrence date (e.g. "23 Jun"),
    // not EffectiveDueDate — which is a forward-looking "what's due next"
    // computation that's already rolled past the date that was really paid.
    public List<BillOccurrencePreview> GetPaidBillOccurrences(int lookbackDays = 90)
    {
        var cutoff = DateTime.Today.AddDays(-lookbackDays);
        var billMap = Bills.ToDictionary(b => b.Id);
        return BillStatuses
            .Where(s => s.IsPaid && s.DueDate.Date >= cutoff && billMap.ContainsKey(s.BillId))
            .Select(s => new BillOccurrencePreview(billMap[s.BillId], s.DueDate))
            .OrderByDescending(o => o.DueDate)
            .ToList();
    }

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

        // Sum from the occurrence projection (not BillsDueBeforePayday) so a bill
        // with multiple missed/unpaid cycles before payday counts each one,
        // instead of collapsing them into a single bill-level total.
        TotalBillsDue = GetOutstandingBillOccurrences()
            .Where(o => o.DueDate.Date <= payEnd)
            .Sum(o => o.Bill.AmountDollars);

        // Per bill-account: bills due strictly before payday vs the account's current balance.
        BillAccountShortfalls = Bills
            .Where(b => !IsBillPaid(b) && b.EffectiveDueDate.Date < NextPayDate.Date)
            .GroupBy(b => b.AccountId)
            .Select(g =>
            {
                var balance = GetAccountBalance(g.Key);
                var due = g.Sum(b => b.AmountDollars);
                return new BillAccountShortfall
                {
                    AccountName = g.First().AccountName,
                    CurrentBalance = balance,
                    DueBeforePayday = due,
                    BillCount = g.Count()
                };
            })
            .Where(s => s.Needed > 0)
            .OrderByDescending(s => s.Needed)
            .ToList();
    }

    private const string StreakSettingKey = "GameStreak";

    private void ComputeStreaks()
    {
        Streak = GetSettingJson<StreakState>(StreakSettingKey) ?? new StreakState();

        var newlyCompletedWeek = TryGetNewlyCompletedWeek(Streak.LastEvaluatedWeekStart);
        if (newlyCompletedWeek is null) return;

        if (DidWeekPassBudget(1))
        {
            Streak.CurrentStreakWeeks++;
        }
        else if (Streak.FreezesAvailable > 0 && Streak.CurrentStreakWeeks > 0)
        {
            // Spend a freeze to protect an existing streak rather than resetting it —
            // gives the XP spent buying a freeze an actual payoff.
            Streak.FreezesAvailable--;
            Streak.LastFreezeUsedWeekStart = newlyCompletedWeek;
        }
        else
        {
            Streak.CurrentStreakWeeks = 0;
        }
        _streakRecordBrokenThisCompute = Streak.CurrentStreakWeeks > Streak.BestStreakWeeks && Streak.CurrentStreakWeeks > 0;
        Streak.BestStreakWeeks = Math.Max(Streak.BestStreakWeeks, Streak.CurrentStreakWeeks);
        Streak.LastEvaluatedWeekStart = newlyCompletedWeek;
        PersistGameStateJson(StreakSettingKey, Streak);
    }

    private bool _streakRecordBrokenThisCompute;

    private const int StreakFreezeCostXp = 100;

    public async Task<bool> BuyStreakFreezeAsync()
    {
        if (Xp.TotalXp < StreakFreezeCostXp) return false;
        Xp.TotalXp -= StreakFreezeCostXp;
        Streak.FreezesAvailable++;
        await SaveSettingJsonAsync(XpSettingKey, Xp);
        await SaveSettingJsonAsync(StreakSettingKey, Streak);
        Compute();
        OnChange?.Invoke();
        return true;
    }

    private const string XpSettingKey = "GameXp";

    private void ComputeXp()
    {
        Xp = GetSettingJson<XpState>(XpSettingKey) ?? new XpState();
        var changed = false;

        // +5 XP once per day the app is opened with a transaction already logged that day.
        if (Xp.LastDailyLoginAward?.Date != DateTime.Today && Transactions.Any(t => t.Date.Date == DateTime.Today))
        {
            Xp.TotalXp += 5;
            Xp.LastDailyLoginAward = DateTime.Today;
            changed = true;
        }

        // +20 XP once per completed week spent within the safe-to-spend budget.
        var newlyCompletedWeek = TryGetNewlyCompletedWeek(Xp.LastEvaluatedWeekStart);
        if (newlyCompletedWeek is not null)
        {
            if (DidWeekPassBudget(1)) Xp.TotalXp += 20;
            Xp.LastEvaluatedWeekStart = newlyCompletedWeek;
            changed = true;
        }

        // +15 XP once per bill paid before its due date.
        var earlyPaidStatuses = BillStatuses
            .Where(s => s.IsPaid && s.PaidOn is not null && s.PaidOn.Value.Date < s.DueDate.Date && !Xp.AwardedBillStatusIds.Contains(s.Id))
            .ToList();
        if (earlyPaidStatuses.Count > 0)
        {
            Xp.TotalXp += 15 * earlyPaidStatuses.Count;
            Xp.AwardedBillStatusIds.AddRange(earlyPaidStatuses.Select(s => s.Id));
            changed = true;
        }

        if (changed) PersistGameStateJson(XpSettingKey, Xp);
    }

    private const string WeeklyChallengeSettingKey = "GameWeeklyChallengeList";

    private void ComputeWeeklyChallenge()
    {
        WeeklyChallenges = GetSettingJson<List<WeeklyChallengeState>>(WeeklyChallengeSettingKey) ?? new();
        var currentWeekStart = GetIsoWeekStart(DateTime.Today);
        var isCurrentWeek = WeeklyChallenges.Count > 0 && WeeklyChallenges[0].WeekStart.Date == currentWeekStart.Date;
        var staleCategoryNames = WeeklyChallenges
            .Where(c => string.Equals(c.CategoryName, "Income", StringComparison.OrdinalIgnoreCase)
                || TransactionClassification.IsInternalMovementCategory(c.CategoryName))
            .Any();

        if (isCurrentWeek && !staleCategoryNames) return;

        var xpChanged = false;

        // Lock in the outcome of each challenge that's ending and award XP, before replacing them.
        // Skip this when we're regenerating mid-week because a cached category was stale —
        // the week hasn't actually ended, so there's no outcome to lock in yet.
        if (isCurrentWeek == false)
        {
            foreach (var challenge in WeeklyChallenges)
            {
                if (challenge.Passed is null)
                {
                    var spentLastWeek = string.IsNullOrEmpty(challenge.CategoryName)
                        ? GetWeekSpending(1)
                        : Transactions
                            .Where(t => t.Date.Date >= challenge.WeekStart && t.Date.Date < currentWeekStart
                                && t.AmountCents < 0
                                && string.Equals(t.CategoryName, challenge.CategoryName, StringComparison.OrdinalIgnoreCase))
                            .Sum(t => Math.Abs(t.AmountDollars));
                    challenge.Passed = spentLastWeek <= challenge.TargetAmount;
                }
                if (challenge.Passed == true && !challenge.XpAwarded)
                {
                    Xp.TotalXp += challenge.XpReward;
                    challenge.XpAwarded = true;
                    xpChanged = true;
                }
            }
        }

        if (xpChanged) PersistGameStateJson(XpSettingKey, Xp);

        WeeklyChallenges = GenerateWeeklyChallenges(currentWeekStart);
        PersistGameStateJson(WeeklyChallengeSettingKey, WeeklyChallenges);
    }

    private List<WeeklyChallengeState> GenerateWeeklyChallenges(DateTime weekStart)
    {
        var lastWeekStart = weekStart.AddDays(-7);
        var topCategories = Transactions
            .Where(t => t.Date.Date >= lastWeekStart && t.Date.Date < weekStart
                && t.AmountCents < 0
                && !string.Equals(t.CategoryName, "Income", StringComparison.OrdinalIgnoreCase)
                && !TransactionClassification.IsInternalMovementCategory(t.CategoryName))
            .GroupBy(t => t.CategoryName)
            .Select(g => new { Category = g.Key, Spent = g.Sum(t => Math.Abs(t.AmountDollars)), Count = g.Count() })
            .Where(g => g.Count >= 2 && g.Spent > 0)
            .OrderByDescending(g => g.Spent)
            .Take(2)
            .ToList();

        var challenges = new List<WeeklyChallengeState>();
        var xpRewards = new[] { 25, 20 };
        for (var i = 0; i < topCategories.Count; i++)
        {
            var cat = topCategories[i];
            var target = Math.Round(cat.Spent * 0.9m / 5m) * 5m;
            challenges.Add(new WeeklyChallengeState
            {
                WeekStart = weekStart,
                ChallengeKey = $"beat-category:{cat.Category}",
                Title = $"Beat last week's {cat.Category} spend",
                Description = $"Keep {cat.Category} spending under {target:C} this week (last week: {cat.Spent:C}).",
                TargetAmount = target,
                CategoryName = cat.Category,
                XpReward = xpRewards[i]
            });
        }

        // Always include the overall safe-to-spend challenge so there's at least one even with no clear top categories.
        challenges.Add(new WeeklyChallengeState
        {
            WeekStart = weekStart,
            ChallengeKey = "stay-under-safe-to-spend",
            Title = "Stay under your safe-to-spend amount",
            Description = $"Keep total spending under {BudgetSafeToSpendAmount:C} this week.",
            TargetAmount = BudgetSafeToSpendAmount,
            CategoryName = null,
            XpReward = 30
        });

        return challenges;
    }

    private const string RoundUpSettingKey = "GameRoundUp";

    private void ComputeRoundUp()
    {
        RoundUp = GetSettingJson<RoundUpState>(RoundUpSettingKey) ?? new RoundUpState();
        if (!RoundUp.Enabled) return;

        var newSpends = Transactions
            .Where(t => t.Id > RoundUp.LastProcessedTransactionId && t.AmountCents < 0)
            .ToList();
        if (newSpends.Count == 0) return;

        var roundTo = Math.Max(RoundUp.RoundToCents, 1);
        var addedCents = newSpends.Sum(t =>
        {
            var spentCents = -t.AmountCents;
            var remainder = spentCents % roundTo;
            return remainder == 0 ? 0 : roundTo - remainder;
        });

        RoundUp.AccumulatedCents += addedCents;
        RoundUp.LastProcessedTransactionId = Transactions.Max(t => t.Id);
        PersistGameStateJson(RoundUpSettingKey, RoundUp);
    }

    public async Task SetRoundUpEnabledAsync(bool enabled, int roundToCents)
    {
        RoundUp.Enabled = enabled;
        RoundUp.RoundToCents = roundToCents;
        if (RoundUp.LastProcessedTransactionId == 0 && Transactions.Count > 0)
            RoundUp.LastProcessedTransactionId = Transactions.Max(t => t.Id);
        await SaveSettingJsonAsync(RoundUpSettingKey, RoundUp);
        Compute();
        OnChange?.Invoke();
    }

    public async Task SweepRoundUpToGoalAsync(int savingsGoalId)
    {
        var goal = SavingsGoals.FirstOrDefault(g => g.Id == savingsGoalId);
        if (goal is null || RoundUp.AccumulatedCents <= 0) return;

        var updated = new SavingsGoal
        {
            Id = goal.Id,
            Name = goal.Name,
            TargetCents = goal.TargetCents,
            CurrentCents = goal.CurrentCents + RoundUp.AccumulatedCents,
            WeeklyContributionCents = goal.WeeklyContributionCents,
            TargetDate = goal.TargetDate,
            GroupName = goal.GroupName,
            TargetStartDate = goal.TargetStartDate,
            TargetStartingBalanceCents = goal.TargetStartingBalanceCents,
            Emoji = goal.Emoji
        };
        await UpdateSavingsGoalAsync(updated);

        RoundUp.AccumulatedCents = 0;
        await SaveSettingJsonAsync(RoundUpSettingKey, RoundUp);
        Compute();
        OnChange?.Invoke();
    }

    private const string BadgesSettingKey = "GameBadges";

    private void ComputeBadges()
    {
        Badges = GetSettingJson<BadgeState>(BadgesSettingKey) ?? new BadgeState();
        var newlyUnlocked = new List<BadgeDefinition>();

        foreach (var badge in BadgeCatalog.All)
        {
            if (Badges.UnlockedBadgeIds.Contains(badge.Id)) continue;
            if (!IsBadgeEarned(badge.Id)) continue;

            Badges.UnlockedBadgeIds.Add(badge.Id);
            Badges.UnlockedDates[badge.Id] = DateTime.Today;
            newlyUnlocked.Add(badge);
        }

        if (newlyUnlocked.Count > 0) PersistGameStateJson(BadgesSettingKey, Badges);
        _newlyUnlockedBadges = newlyUnlocked;
    }

    private List<BadgeDefinition> _newlyUnlockedBadges = new();

    private bool IsBadgeEarned(string badgeId) => badgeId switch
    {
        "emergency-1k" => Accounts.Where(a => a.Type == AccountType.Savings)
            .Any(a => GetAccountBalance(a.Id) >= 1000),
        "debt-half" => Debts.Any(d => d.OriginalBalanceCents > 0 && d.BalanceCents > 0 && d.BalanceCents <= d.OriginalBalanceCents / 2),
        "debt-cleared" => Debts.Any(d => d.OriginalBalanceCents > 0 && d.BalanceCents <= 0),
        "streak-12" => Streak.BestStreakWeeks >= 12,
        "no-spend-7" => NoSpendMode && NoSpendModeSince is not null && (DateTime.Today - NoSpendModeSince.Value.Date).TotalDays >= 7,
        _ => false
    };

    public string? GetSetting(string key) =>
        AppSettings.FirstOrDefault(s => s.Key == key)?.Value;

    public T? GetSettingJson<T>(string key)
    {
        var raw = GetSetting(key);
        if (string.IsNullOrWhiteSpace(raw)) return default;
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(raw); }
        catch { return default; }
    }

    public Task SaveSettingJsonAsync<T>(string key, T value) =>
        SaveSettingAsync(key, System.Text.Json.JsonSerializer.Serialize(value));

    /// <summary>
    /// Updates the in-memory settings list immediately and fires off the real
    /// persist in the background. Compute() runs synchronously and often, so a
    /// gamification state change (e.g. a streak locking in) must be visible to the
    /// very next Compute() call rather than only after the async DB write returns —
    /// otherwise a second mutation in the same tick could re-evaluate the same
    /// week-close transition and double-award it.
    /// </summary>
    private void PersistGameStateJson<T>(string key, T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        var existing = AppSettings.FirstOrDefault(s => s.Key == key);
        if (existing is not null) existing.Value = json;
        else AppSettings.Add(new AppSetting { Key = key, Value = json });
        _ = SaveSettingAsync(key, json);
    }

    /// <summary>Whether the ISO week starting Monday, N weeks ago, stayed within the safe-to-spend budget.</summary>
    public bool DidWeekPassBudget(int weeksAgo) =>
        GetWeekSpending(weeksAgo) <= BudgetSafeToSpendAmount;

    private static DateTime GetIsoWeekStart(DateTime date)
    {
        var dow = (int)date.DayOfWeek;
        return date.Date.AddDays(-(dow == 0 ? 6 : dow - 1));
    }

    /// <summary>
    /// Returns the most recently completed ISO week's start date if it hasn't been
    /// evaluated yet (lastEvaluated is older than it), otherwise null. Shared by every
    /// feature that locks in a pass/fail result once per week so edits to old
    /// transactions can't make a streak/challenge/XP award jump around after the fact.
    /// </summary>
    private static DateTime? TryGetNewlyCompletedWeek(DateTime? lastEvaluatedWeekStart)
    {
        var lastCompletedWeekStart = GetIsoWeekStart(DateTime.Today).AddDays(-7);
        if (lastEvaluatedWeekStart is not null && lastEvaluatedWeekStart.Value.Date >= lastCompletedWeekStart)
            return null;
        return lastCompletedWeekStart;
    }

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

        // A flat ±3 day grace window is a small sliver of a yearly bill's cycle but
        // ~43% of a weekly one (21% fortnightly) — wide enough to let the previous
        // cycle's "paid" status leak into the next fresh occurrence. Scale down for
        // short cycles; the window only needs to cover sync latency (a day or two),
        // not a fraction of the bill's own period.
        var graceDays = Math.Max(1, Math.Min(3, ApproxFrequencyDays(bill.Frequency) / 7));

        // 3. Tight date-drift fallback: a status within ±graceDays of the effective
        //    due date covers cases where the PC recorded a slightly different date
        //    (e.g. same-day payment). Intentionally narrow so a previous cycle's
        //    payment (e.g. paid June 2, due June 9) is NOT treated as "paid for this cycle".
        var latest = BillStatuses
            .Where(s => s.BillId == bill.Id)
            .OrderByDescending(s => s.DueDate)
            .FirstOrDefault();
        if (latest is not null)
        {
            var daysFromEffective = Math.Abs((latest.DueDate.Date - effectiveDue.Date).TotalDays);
            if (daysFromEffective <= graceDays) return latest.IsPaid;
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
            if (Math.Abs((DateTime.Today - prevDue.Date).TotalDays) <= graceDays)
            {
                var prevStatus = BillStatuses
                    .FirstOrDefault(s => s.BillId == bill.Id && s.DueDate.Date == prevDue.Date);
                if (prevStatus is not null) return prevStatus.IsPaid;
                if (latest is not null)
                {
                    var daysToPrev = Math.Abs((latest.DueDate.Date - prevDue.Date).TotalDays);
                    if (daysToPrev <= graceDays) return latest.IsPaid;
                }
            }
        }
        return false;
    }

    private static int ApproxFrequencyDays(BillFrequency f) => f switch
    {
        BillFrequency.Weekly      => 7,
        BillFrequency.Fortnightly => 14,
        BillFrequency.Monthly     => 30,
        BillFrequency.Quarterly   => 91,
        BillFrequency.Yearly      => 365,
        _                         => 30
    };

    public List<Transaction> GetTransactionsForPeriod(DateTime from, DateTime to) =>
        Transactions.Where(t => t.Date.Date >= from && t.Date.Date <= to).ToList();

    public (DateTime from, DateTime to) GetCurrentPeriod()
    {
        if (SummaryPeriod == "Weekly")
            return GetCurrentPayCycle();
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

    private bool WeekHasTransactions(int weeksAgo)
    {
        var today = DateTime.Today;
        var dow = (int)today.DayOfWeek;
        var monday = today.AddDays(-(dow == 0 ? 6 : dow - 1));
        var from = monday.AddDays(-7 * weeksAgo);
        var to = from.AddDays(6);
        return Transactions.Any(t => t.Date.Date >= from && t.Date.Date <= to);
    }

    /// <summary>The lowest-spend completed week in the lookback window (ignoring weeks with no transactions at all).</summary>
    public (decimal Amount, DateTime WeekStart) GetBestWeekEver(int lookbackWeeks = 52)
    {
        var today = DateTime.Today;
        var dow = (int)today.DayOfWeek;
        var monday = today.AddDays(-(dow == 0 ? 6 : dow - 1));

        decimal? bestAmount = null;
        var bestWeekStart = monday;
        for (var weeksAgo = 1; weeksAgo <= lookbackWeeks; weeksAgo++)
        {
            if (!WeekHasTransactions(weeksAgo)) continue;
            var spend = GetWeekSpending(weeksAgo);
            if (bestAmount is null || spend < bestAmount)
            {
                bestAmount = spend;
                bestWeekStart = monday.AddDays(-7 * weeksAgo);
            }
        }
        return (bestAmount ?? 0, bestWeekStart);
    }

    /// <summary>Positive = spending more than last week, negative = spending less.</summary>
    public decimal SpendDeltaVsLastWeek => GetWeekSpending(0) - GetWeekSpending(1);

    /// <summary>Positive = spending more than last month, negative = spending less.</summary>
    public decimal SpendDeltaVsLastMonth
    {
        get
        {
            var trend = GetMonthlyTrend(2);
            if (trend.Count < 2) return 0;
            return trend[^1].Spending - trend[^2].Spending;
        }
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

    public record SmartBudgetSuggestion(decimal Income, decimal Bills, decimal Essentials, decimal Unplanned, decimal Savings);

    // Heuristic budget suggestion built from real history, not a model call:
    // bills = real weekly-equivalent of tracked Bills, essentials/unplanned = trailing
    // 8-week averages of essential vs. discretionary spend, savings = whatever's left
    // of income once those are covered.
    public SmartBudgetSuggestion GetSmartBudgetSuggestion(int weeksBack = 8)
    {
        var to = DateTime.Today;
        var from = to.AddDays(-7 * weeksBack);
        var weeks = Math.Max(weeksBack, 1);

        var income = WeeklyIncome > 0 ? WeeklyIncome : GetAverageWeeklyIncome(from, to);

        var bills = Bills.Sum(b => GetWeeklyEquivalent(b.AmountDollars, b.Frequency));

        var discretionary = Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents < 0
                        && !IsInternalMovement(t) && !IsBudgetedBillTransaction(t) && !IsLent(t.Id))
            .ToList();
        var essentials = Math.Round(discretionary.Where(t => IsEssentialCategory(t.CategoryName)).Sum(t => Math.Abs(t.AmountDollars)) / weeks, 0);
        var unplanned = Math.Round(discretionary.Where(t => !IsEssentialCategory(t.CategoryName)).Sum(t => Math.Abs(t.AmountDollars)) / weeks, 0);

        var savings = Math.Max(income - bills - essentials - unplanned, 0);

        return new SmartBudgetSuggestion(Math.Round(income, 0), Math.Round(bills, 0), essentials, unplanned, Math.Round(savings, 0));
    }

    private decimal GetAverageWeeklyIncome(DateTime from, DateTime to)
    {
        var weeks = Math.Max((to - from).Days / 7.0, 1);
        var total = Transactions
            .Where(t => t.Date.Date >= from && t.Date.Date <= to && t.AmountCents > 0 && !IsInternalMovement(t) && !t.IsReimbursement)
            .Sum(t => t.AmountDollars);
        return (decimal)((double)total / weeks);
    }

    private static decimal GetWeeklyEquivalent(decimal amount, BillFrequency frequency) => frequency switch
    {
        BillFrequency.Weekly => amount,
        BillFrequency.Fortnightly => amount / 2m,
        BillFrequency.Monthly => amount * 12m / 52m,
        BillFrequency.Quarterly => amount * 4m / 52m,
        BillFrequency.Yearly => amount / 52m,
        _ => 0
    };

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
    private const string IgnoredCleanupMerchantsSettingKey = "IgnoredCleanupMerchants";

    private List<string> GetIgnoredStringList(string key)
    {
        var json = GetSetting(key);
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

    private List<string> GetIgnoredSubscriptions() => GetIgnoredStringList(IgnoredSubscriptionsSettingKey);

    private List<string> GetIgnoredCleanupMerchants() => GetIgnoredStringList(IgnoredCleanupMerchantsSettingKey);

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

    // Flags a merchant's most recent charge when it's well above that same merchant's
    // own historical median — catches "your usual $20 Telstra bill just became $60"
    // in a way the daily/weekly pace checks above can't, since those only look at
    // aggregate spend, not any one merchant's normal range. Same dual relative+flat
    // threshold shape as the daily/weekly pace checks in ComputeProactiveInsights.
    public List<SpendingAnomaly> GetSpendingAnomalies()
    {
        var cutoff = DateTime.Today.AddDays(-14);
        var anomalies = new List<SpendingAnomaly>();

        foreach (var group in Transactions
            .Where(t => t.AmountCents < 0 && !IsInternalMovement(t) && !string.IsNullOrWhiteSpace(t.Description))
            .GroupBy(t => NormalizeRecurringDescription(t.Description)))
        {
            var ordered = group.OrderByDescending(t => t.Date).ToList();
            if (ordered.Count < 4) continue; // need 3+ prior occurrences besides the most recent

            var mostRecent = ordered[0];
            if (mostRecent.Date.Date < cutoff) continue; // stale — don't resurface an old one-off

            var priorAmounts = ordered.Skip(1).Select(t => Math.Abs(t.AmountDollars)).OrderBy(a => a).ToList();
            var median = priorAmounts[priorAmounts.Count / 2];
            if (median <= 0) continue;

            var recentAmount = Math.Abs(mostRecent.AmountDollars);
            var threshold = Math.Max(median * 1.6m, median + 10m);
            if (recentAmount < threshold) continue;

            anomalies.Add(new SpendingAnomaly
            {
                Merchant = group.Key,
                TransactionId = mostRecent.Id,
                RecentAmount = recentAmount,
                TypicalAmount = median,
                Date = mostRecent.Date
            });
        }

        return anomalies
            .OrderByDescending(a => a.RecentAmount - a.TypicalAmount)
            .Take(5)
            .ToList();
    }

    public List<TransactionCleanupSuggestion> GetTransactionCleanupSuggestions()
    {
        var ignored = GetIgnoredCleanupMerchants().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Transactions
            .Where(t => t.AmountCents < 0 && !IsInternalMovement(t) && !string.IsNullOrWhiteSpace(t.Description))
            .GroupBy(t => NormalizeRecurringDescription(t.Description))
            .Where(g => !ignored.Contains(g.Key))
            .Select(g => BuildCleanupSuggestion(g.ToList()))
            .Where(s => s is not null)
            .Cast<TransactionCleanupSuggestion>()
            .OrderByDescending(s => s.AffectedCount)
            .ThenByDescending(s => s.AffectedAmount)
            .Take(8)
            .ToList();
    }

    public async Task IgnoreTransactionCleanupSuggestionAsync(string merchant)
    {
        var ignored = GetIgnoredCleanupMerchants();
        if (!ignored.Contains(merchant, StringComparer.OrdinalIgnoreCase))
        {
            ignored.Add(merchant);
            await SaveSettingAsync(IgnoredCleanupMerchantsSettingKey, System.Text.Json.JsonSerializer.Serialize(ignored));
        }
    }

    public List<MerchantSpendWatchItem> GetMerchantSpendWatchlist(int days = 30, int count = 8)
    {
        var from = DateTime.Today.AddDays(-Math.Max(days, 1));
        return Transactions
            .Where(t => t.Date.Date >= from &&
                        t.AmountCents < 0 &&
                        !IsInternalMovement(t) &&
                        !IsBudgetedBillTransaction(t) &&
                        !string.IsNullOrWhiteSpace(t.Description))
            .GroupBy(t => NormalizeRecurringDescription(t.Description))
            .Select(g => new MerchantSpendWatchItem
            {
                Merchant = g.Key,
                Amount = g.Sum(t => Math.Abs(t.AmountDollars)),
                UnnecessaryAmount = g.Where(t => t.IsUnnecessary).Sum(t => Math.Abs(t.AmountDollars)),
                Count = g.Count(),
                LastSeen = g.Max(t => t.Date.Date)
            })
            .Where(item => item.Amount > 0)
            .OrderByDescending(item => item.Amount)
            .ThenByDescending(item => item.Count)
            .Take(count)
            .ToList();
    }

    public async Task<int> MarkMerchantUnnecessaryAsync(string merchant, int days = 30)
    {
        var from = DateTime.Today.AddDays(-Math.Max(days, 1));
        var rows = Transactions
            .Where(t => t.Date.Date >= from &&
                        t.AmountCents < 0 &&
                        !t.IsUnnecessary &&
                        !IsInternalMovement(t) &&
                        NormalizeRecurringDescription(t.Description) == merchant)
            .ToList();

        foreach (var transaction in rows)
        {
            transaction.IsUnnecessary = true;
            await db.PutAsync("transactions", transaction);
            QueueUpdatedTransaction(transaction);
            await db.SetTransactionOverrideAsync(transaction);
        }

        if (rows.Count > 0)
        {
            Compute();
            OnChange?.Invoke();
            ScheduleSyncSoon();
        }

        return rows.Count;
    }

    public async Task<int> ApplyTransactionCleanupSuggestionAsync(TransactionCleanupSuggestion suggestion)
    {
        var rows = Transactions
            .Where(t => NormalizeRecurringDescription(t.Description) == suggestion.Merchant &&
                        t.CategoryId != suggestion.SuggestedCategoryId &&
                        t.AmountCents < 0 &&
                        !IsInternalMovement(t))
            .OrderByDescending(t => t.Date)
            .ToList();

        foreach (var transaction in rows)
            await UpdateTransactionCategoryAsync(transaction.Id, suggestion.SuggestedCategoryId);

        return rows.Count;
    }

    public List<BillIntelligenceSuggestion> GetBillIntelligenceSuggestions()
    {
        var suggestions = new List<BillIntelligenceSuggestion>();

        foreach (var recurring in GetRecurringPayments().Where(r => !r.IsAlreadyBill).Take(6))
        {
            suggestions.Add(new BillIntelligenceSuggestion
            {
                Kind = "NewBill",
                Title = recurring.Name,
                Message = $"{recurring.AverageAmount:C} {recurring.Frequency.ToLowerInvariant()}, next expected {recurring.NextDueDisplay}.",
                ActionLabel = "Create bill",
                RecurringPayment = recurring
            });
        }

        // GroupBy-then-First instead of ToDictionary directly: GetRecurringPayments groups
        // by NormalizeRecurringDescription with the default (case-sensitive) comparer, so two
        // real transactions for the same merchant with different casing (common in bank-imported
        // descriptions, e.g. "Netflix.com" vs "NETFLIX.COM") can come back as two separate entries
        // whose Names only collide once compared case-insensitively — ToDictionary would throw on
        // that collision instead of just picking one.
        var recurringByName = GetRecurringPayments()
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var bill in Bills.Where(b => !StateIsGeneratedBillNameEmpty(b)).OrderBy(b => b.EffectiveDueDate))
        {
            var key = NormalizeRecurringDescription(bill.Name);
            if (!recurringByName.TryGetValue(key, out var recurring)) continue;

            var expected = recurring.AverageAmount;
            var current = bill.AmountDollars;
            var delta = Math.Abs(current - expected);
            if (expected > 0 && delta >= Math.Max(5m, expected * 0.15m))
            {
                suggestions.Add(new BillIntelligenceSuggestion
                {
                    Kind = "AmountChanged",
                    Title = $"{bill.Name} amount changed",
                    Message = $"Bill is {current:C}, recent average is {expected:C}.",
                    ActionLabel = "Update amount",
                    RecurringPayment = recurring,
                    BillId = bill.Id
                });
            }
            // Distinct from AmountChanged above: a slow creep raises the average right
            // along with it, so a bill that's gone from $40 to $52 over a year of small
            // rises never trips the vs-average check. Comparing against the all-time low
            // instead catches that gradual drift, which is exactly the shape a "should I
            // negotiate/switch providers" nudge is for.
            else if (recurring.MinAmount > 0 && current >= recurring.MinAmount * 1.25m && (current - recurring.MinAmount) >= 10m)
            {
                suggestions.Add(new BillIntelligenceSuggestion
                {
                    Kind = "PriceCreep",
                    Title = $"{bill.Name} has crept up",
                    Message = $"Now {current:C} — as low as {recurring.MinAmount:C} before. Might be worth a quick review or a call to negotiate.",
                    ActionLabel = "Review in Bills",
                    RecurringPayment = recurring,
                    BillId = bill.Id
                });
            }
        }

        return suggestions.Take(10).ToList();
    }

    public async Task<bool> CreateBillFromRecurringAsync(RecurringPayment recurring)
    {
        var accountId = Accounts.FirstOrDefault(a => string.Equals(a.Name, recurring.AccountName, StringComparison.OrdinalIgnoreCase))?.Id
            ?? Accounts.FirstOrDefault()?.Id
            ?? 0;
        if (accountId == 0 || !Enum.TryParse<BillFrequency>(recurring.Frequency, out var frequency)) return false;

        await AddBillAsync(new Bill
        {
            Name = recurring.Name,
            AccountId = accountId,
            AmountDollars = recurring.AverageAmount,
            DueDate = recurring.NextExpected,
            NextPayDate = recurring.NextExpected,
            Frequency = frequency,
            IsAutoPay = true,
            IsCreatedFromRecurringPayment = true,
            PaymentMatchText = recurring.Name
        });
        return true;
    }

    public async Task<bool> UpdateBillAmountFromRecurringAsync(int billId, RecurringPayment recurring)
    {
        var bill = Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null) return false;

        bill.AmountDollars = recurring.AverageAmount;
        bill.PaymentMatchText = string.IsNullOrWhiteSpace(bill.PaymentMatchText) ? recurring.Name : bill.PaymentMatchText;
        await UpdateBillAsync(bill);
        return true;
    }

    private TransactionCleanupSuggestion? BuildCleanupSuggestion(List<Transaction> rows)
    {
        if (rows.Count < 2) return null;

        var category = rows
            .Where(t => t.CategoryId != 0 && !string.IsNullOrWhiteSpace(t.CategoryName))
            .GroupBy(t => t.CategoryId)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(t => t.Date))
            .FirstOrDefault();
        if (category is null) return null;

        var suggestedCategoryId = category.Key;
        var suggestedCategoryName = Categories.FirstOrDefault(c => c.Id == suggestedCategoryId)?.Name
            ?? category.First().CategoryName;
        var affected = rows.Where(t => t.CategoryId != suggestedCategoryId).ToList();
        if (affected.Count == 0) return null;

        return new TransactionCleanupSuggestion
        {
            Merchant = NormalizeRecurringDescription(rows[0].Description),
            SuggestedCategoryId = suggestedCategoryId,
            SuggestedCategoryName = suggestedCategoryName,
            AffectedCount = affected.Count,
            TotalSeen = rows.Count,
            AffectedAmount = affected.Sum(t => Math.Abs(t.AmountDollars))
        };
    }

    private static bool StateIsGeneratedBillNameEmpty(Bill bill) =>
        string.IsNullOrWhiteSpace(bill.Name);

    private void ComputeProactiveInsights()
    {
        var insights = new List<ProactiveInsight>();
        var forecast = GetForecastTotals();

        if (forecast.Now < 0)
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"balance-overdrawn:{DateTime.Today:yyyyMMdd}",
                Severity = ProactiveInsightSeverity.Critical,
                Title = "Balance is overdrawn",
                Message = $"You are at {forecast.Now:C}. Hold new spending until money lands.",
                ActionUrl = "accounts"
            });
        }
        else if (forecast.AfterBills < 0)
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"balance-risk:{DateTime.Today:yyyyMMdd}",
                Severity = ProactiveInsightSeverity.Critical,
                Title = "Bills may take you negative",
                Message = $"After unpaid bills before payday, the forecast is {forecast.AfterBills:C}.",
                ActionUrl = "bills"
            });
        }
        else
        {
            // Both checks above look at the end state only, so a mid-cycle dip that
            // recovers by payday (e.g. two bills land close together early, balance
            // climbs back before NextPayDate) would otherwise go unnoticed until it's
            // already happening.
            var shortfall = GetPaydayShortfallProjection();
            if (shortfall.LowestBalance < 0)
            {
                insights.Add(new ProactiveInsight
                {
                    Key = $"payday-shortfall:{NextPayDate:yyyyMMdd}",
                    Severity = ProactiveInsightSeverity.Warning,
                    Title = "Tight before payday",
                    Message = $"Today looks fine, but you're projected as low as {shortfall.LowestBalance:C} on {shortfall.LowestDate:dd MMM} before payday.",
                    ActionUrl = "budget"
                });
            }
        }

        var todaySpend = GetTodaySpending();
        var avgDaily = GetAvgDailySpending();
        var todayDiscretionary = GetDiscretionarySpendingForPeriod(DateTime.Today, DateTime.Today);
        if (NoSpendMode)
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"no-spend:{DateTime.Today:yyyyMMdd}",
                Severity = todayDiscretionary > 0 ? ProactiveInsightSeverity.Warning : ProactiveInsightSeverity.Info,
                Title = todayDiscretionary > 0 ? "No-spend mode has spending" : "No-spend mode is protecting today",
                Message = todayDiscretionary > 0
                    ? $"{todayDiscretionary:C} non-bill spending today. Review it or turn no-spend off."
                    : "Forecasts assume $0 discretionary spending while this is on.",
                ActionUrl = "transactions"
            });
        }
        else if (BudgetSafeToSpendAmount <= 0 && WeeklyIncome > 0)
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"no-spend-suggested:{DateTime.Today:yyyyMMdd}",
                Severity = ProactiveInsightSeverity.Warning,
                Title = "No-spend mode recommended",
                Message = "Your safe-to-spend buffer is gone for this pay cycle.",
                ActionUrl = "tools"
            });
        }

        if (OutstandingLentDollars > 0)
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"lent-outstanding:{DateTime.Today:yyyyMMdd}",
                Severity = ProactiveInsightSeverity.Info,
                Title = "Money owed back",
                Message = $"{OutstandingLentDollars:C} is marked lent out and excluded from spending.",
                ActionUrl = "budget"
            });
        }

        if (avgDaily > 0 && todaySpend >= Math.Max(avgDaily * 1.75m, avgDaily + 25m))
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"daily-spend:{DateTime.Today:yyyyMMdd}",
                Severity = ProactiveInsightSeverity.Warning,
                Title = "Spending is running hot today",
                Message = $"{todaySpend:C} today vs a usual daily pace of about {avgDaily:C}.{TopCategorySuffix(DateTime.Today, DateTime.Today)}",
                ActionUrl = "transactions"
            });
        }

        var thisWeek = GetWeekSpending(0);
        var priorWeeks = Enumerable.Range(1, 4).Select(GetWeekSpending).Where(v => v > 0).ToList();
        if (DateTime.Today.DayOfWeek != DayOfWeek.Monday && priorWeeks.Count >= 2)
        {
            var avgWeek = priorWeeks.Average();
            if (avgWeek > 0 && thisWeek >= Math.Max(avgWeek * 1.5m, avgWeek + 75m))
            {
                insights.Add(new ProactiveInsight
                {
                    Key = $"weekly-pace:{StartOfWeek(DateTime.Today):yyyyMMdd}",
                    Severity = ProactiveInsightSeverity.Warning,
                    Title = "This week is above normal",
                    Message = $"{thisWeek:C0} spent this week vs about {avgWeek:C0} usually.{TopCategorySuffix(StartOfWeek(DateTime.Today), DateTime.Today)}",
                    ActionUrl = "transactions"
                });
            }
        }

        var newSubscriptions = GetRecurringPayments()
            .Where(r => !r.IsAlreadyBill && r.NextExpected.Date <= DateTime.Today.AddDays(10))
            .OrderBy(r => r.NextExpected)
            .Take(2)
            .ToList();
        foreach (var sub in newSubscriptions)
        {
            insights.Add(new ProactiveInsight
            {
                Key = $"subscription:{sub.Name.ToLowerInvariant()}",
                Severity = ProactiveInsightSeverity.Info,
                Title = "Possible subscription found",
                Message = $"{sub.Name} looks recurring at about {sub.AverageAmount:C}/{FrequencyShort(sub.Frequency)}.",
                ActionUrl = "budget"
            });
        }

        ProactiveInsights = insights
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Title)
            .Take(4)
            .ToList();
    }

    // Folds the Tools-page-only suggestion sources into the same ProactiveInsight
    // shape so they're visible anywhere ProactiveInsights used to be the only signal
    // (Dashboard, local push notifications, the Tools snapshot export) without
    // duplicating their underlying detection logic — Tools.razor's own cards still
    // read GetTransactionCleanupSuggestions/GetMerchantSpendWatchlist/GetBillIntelligenceSuggestions
    // directly for their full Apply/Ignore actions.
    //
    // Each source runs inside its own try/catch: this method is called from Compute(),
    // which every write path (e.g. MarkBillPaidAsync) calls before notifying the UI —
    // a real-world data edge case (e.g. one of these LINQ queries throwing on an
    // unexpected shape) must never be able to abort Compute() and silently block the
    // rest of the app from re-rendering. A "nothing happened" bug report after tapping
    // an unrelated button (e.g. on the Bills page) is exactly what that would look like.
    private void ComputeUnifiedSmartSignals()
    {
        var signals = new List<ProactiveInsight>(ProactiveInsights);

        try
        {
            foreach (var suggestion in GetTransactionCleanupSuggestions().Take(3))
            {
                signals.Add(new ProactiveInsight
                {
                    Key = $"cleanup:{suggestion.Merchant}",
                    Severity = ProactiveInsightSeverity.Info,
                    Title = $"{suggestion.Merchant} can be recategorized",
                    Message = $"{suggestion.AffectedCount} of {suggestion.TotalSeen} transactions could become {suggestion.SuggestedCategoryName}.",
                    ActionUrl = "tools"
                });
            }
        }
        catch { /* supplementary signal source — never block Compute() over this */ }

        try
        {
            foreach (var item in GetMerchantSpendWatchlist().Where(i => i.UnnecessaryAmount > 0).Take(2))
            {
                signals.Add(new ProactiveInsight
                {
                    Key = $"watchlist:{item.Merchant}",
                    Severity = ProactiveInsightSeverity.Info,
                    Title = $"{item.Merchant} spend flagged as unnecessary",
                    Message = $"{item.UnnecessaryAmount:C} of {item.Amount:C} from {item.Merchant} in the last 30 days is marked unnecessary.",
                    ActionUrl = "tools"
                });
            }
        }
        catch { }

        try
        {
            foreach (var suggestion in GetBillIntelligenceSuggestions().Take(3))
            {
                signals.Add(new ProactiveInsight
                {
                    Key = $"billintel:{suggestion.Kind}:{suggestion.Title}",
                    Severity = suggestion.Kind == "AmountChanged" ? ProactiveInsightSeverity.Warning : ProactiveInsightSeverity.Info,
                    Title = suggestion.Title,
                    Message = suggestion.Message,
                    ActionUrl = "tools"
                });
            }
        }
        catch { }

        try
        {
            foreach (var anomaly in GetSpendingAnomalies().Take(2))
            {
                signals.Add(new ProactiveInsight
                {
                    Key = $"anomaly:{anomaly.TransactionId}",
                    Severity = ProactiveInsightSeverity.Warning,
                    Title = $"{anomaly.Merchant} charge looks high",
                    Message = $"{anomaly.RecentAmount:C} on {anomaly.Date:dd MMM} vs a usual {anomaly.TypicalAmount:C}.",
                    ActionUrl = "transactions"
                });
            }
        }
        catch { }

        try
        {
            foreach (var badge in _newlyUnlockedBadges)
            {
                signals.Add(new ProactiveInsight
                {
                    Key = $"badge-unlocked:{badge.Id}",
                    Severity = ProactiveInsightSeverity.Info,
                    Title = $"Badge unlocked: {badge.Title}",
                    Message = badge.Description,
                    ActionUrl = "tools"
                });
            }
        }
        catch { }

        try
        {
            // Loss-aversion nudge: warn near the end of the week when a multi-week streak
            // is at risk, rather than only celebrating streaks after the fact.
            var isoDayOfWeek = (int)DateTime.Today.DayOfWeek == 0 ? 7 : (int)DateTime.Today.DayOfWeek;
            var daysLeftInWeek = 7 - isoDayOfWeek;
            if (Streak.CurrentStreakWeeks >= 2 && daysLeftInWeek <= 2 && BudgetSafeToSpendAmount > 0)
            {
                var spentSoFar = GetWeekSpending(0);
                var percentUsed = spentSoFar / BudgetSafeToSpendAmount;
                if (percentUsed >= 0.8m && percentUsed < 1m)
                {
                    var roomLeft = BudgetSafeToSpendAmount - spentSoFar;
                    signals.Add(new ProactiveInsight
                    {
                        Key = $"streak-at-risk:{GetIsoWeekStart(DateTime.Today):yyyyMMdd}",
                        Severity = ProactiveInsightSeverity.Warning,
                        Title = $"Don't break your {Streak.CurrentStreakWeeks}-week streak",
                        Message = $"You have {roomLeft:C} of room left this week — stay under to keep the streak alive.",
                        ActionUrl = "budget"
                    });
                }
            }
        }
        catch { }

        try
        {
            // Give a milestone (new streak record, or a fresh best-week-ever) an actual
            // payoff suggestion instead of just a number going up: nudge the user to
            // bank the round-up total they've already accumulated.
            var bestWeekEver = GetBestWeekEver();
            var lastWeekStart = GetIsoWeekStart(DateTime.Today).AddDays(-7);
            var hitNewBestWeek = RoundUp.Enabled && WeekHasTransactions(1)
                && bestWeekEver.WeekStart.Date == lastWeekStart.Date;
            if ((_streakRecordBrokenThisCompute || hitNewBestWeek) && RoundUp.Enabled && RoundUp.AccumulatedCents > 0 && SavingsGoals.Count > 0)
            {
                signals.Add(new ProactiveInsight
                {
                    Key = $"milestone-roundup-nudge:{GetIsoWeekStart(DateTime.Today):yyyyMMdd}",
                    Severity = ProactiveInsightSeverity.Info,
                    Title = _streakRecordBrokenThisCompute ? "New streak record! Bank it" : "Best week yet — bank it",
                    Message = $"You've got {RoundUp.AccumulatedDollars:C} in round-up savings waiting. Sweep it into a goal to make this milestone count.",
                    ActionUrl = "tools"
                });
            }
        }
        catch { }

        SmartSignals = signals
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Title)
            .Take(8)
            .ToList();
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var dow = (int)date.DayOfWeek;
        return date.Date.AddDays(-(dow == 0 ? 6 : dow - 1));
    }

    // Names which category is actually driving a pace warning — "$120 today" on its
    // own doesn't tell you what to look at, "$120 today, Groceries leads at $80" does.
    private string TopCategorySuffix(DateTime from, DateTime to)
    {
        var top = GetTopCategoriesForPeriod(from, to, 1, excludeBills: true);
        return top.Count > 0 ? $" {top[0].Category} leads at {top[0].Amount:C}." : "";
    }

    private static string FrequencyShort(string frequency) => frequency switch
    {
        "Weekly" => "wk",
        "Fortnightly" => "fortnight",
        "Monthly" => "mo",
        "Quarterly" => "quarter",
        "Yearly" => "yr",
        _ => frequency.ToLowerInvariant()
    };

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

    // ── Category suggestion (live lookup over history — learns from corrections automatically) ──
    public int? SuggestCategoryId(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var normalized = NormalizeRecurringDescription(description);
        var exactMatch = Transactions
            .Where(t => t.CategoryId != 0 && NormalizeRecurringDescription(t.Description) == normalized)
            .GroupBy(t => t.CategoryId)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(t => t.Date))
            .FirstOrDefault();
        if (exactMatch is not null) return exactMatch.Key;

        // No exact repeat — fall back to a first-word match (e.g. "Woolworths Marrickville"
        // vs "Woolworths Bondi"), requiring 2+ matches so a one-off coincidence can't suggest
        // the wrong category.
        var firstWord = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstWord is null || firstWord.Length < 4) return null;

        var wordMatch = Transactions
            .Where(t => t.CategoryId != 0 && t.Description.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.CategoryId)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(t => t.Date))
            .FirstOrDefault();

        return wordMatch is not null && wordMatch.Count() >= 2 ? wordMatch.Key : null;
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

    // GetForecastTotals only looks at the END state (today's balance minus the
    // sum of all bills due before payday) — it can miss a mid-cycle dip that
    // recovers by payday (e.g. two bills land close together on day 5, then a
    // smaller bill on day 12 in a 14-day cycle). This walks bills in due-date
    // order, bleeding the average daily discretionary spend between them, to
    // find the single lowest point and when it happens.
    public (decimal LowestBalance, DateTime LowestDate) GetPaydayShortfallProjection()
    {
        var today   = DateTime.Today;
        var payEnd  = NextPayDate.Date >= today ? NextPayDate.Date : today.AddDays(14);
        var avgDaily = GetAvgDailySpending();

        var billsInWindow = Bills
            .Where(b => !IsBillPaid(b) && b.EffectiveDueDate.Date >= today && b.EffectiveDueDate.Date <= payEnd)
            .OrderBy(b => b.EffectiveDueDate)
            .ToList();

        var balance = TotalBalance;
        var lowest = balance;
        var lowestDate = today;
        var cursor = today;

        void TrackLowest(DateTime date)
        {
            if (balance < lowest)
            {
                lowest = balance;
                lowestDate = date;
            }
        }

        foreach (var bill in billsInWindow)
        {
            var daysElapsed = (bill.EffectiveDueDate.Date - cursor).Days;
            balance -= daysElapsed * avgDaily;
            TrackLowest(bill.EffectiveDueDate.Date);
            balance -= bill.AmountDollars;
            TrackLowest(bill.EffectiveDueDate.Date);
            cursor = bill.EffectiveDueDate.Date;
        }

        var remainingDays = (payEnd - cursor).Days;
        balance -= remainingDays * avgDaily;
        TrackLowest(payEnd);

        return (lowest, lowestDate);
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

    // Per-bill coverage check. Unlike GetAccountForecast (which only looks at bills due
    // before the next payday, since that's what the Accounts page balance projection
    // needs), this sums every unpaid bill on the account due on or before THIS bill's
    // own due date, in due-date order. That way a bill due after the next payday can't
    // be marked "Covered" just because it fell outside that narrower window, and two
    // bills sharing an account get checked against their combined draw on the balance
    // rather than independently.
    public (bool Covered, decimal Shortfall) GetBillCoverage(Bill bill)
    {
        var current = GetAccountBalance(bill.AccountId);
        var dueByThen = Bills
            .Where(b => b.AccountId == bill.AccountId && !IsBillPaid(b) && b.EffectiveDueDate.Date <= bill.EffectiveDueDate.Date)
            .Sum(b => b.AmountDollars);
        var remaining = current - dueByThen;
        return (remaining >= 0, remaining < 0 ? -remaining : 0m);
    }

    // How much to move from the pay account into each other account this payday so
    // it can cover the bills already assigned to it before the next payday — i.e.
    // the same shortfall GetAccountForecast already flags per-account, just
    // collected into one list instead of having to open each account to see it.
    public List<PaydayTransferItem> GetPaydayTransferPlan() =>
        Accounts
            .Where(a => a.Id != PayAccountId && a.Type != AccountType.Credit)
            .Select(a => new PaydayTransferItem
            {
                AccountId = a.Id,
                AccountName = a.Name,
                AccountColorHex = a.ColorHex,
                Amount = -GetAccountForecast(a.Id).AfterBills
            })
            .Where(item => item.Amount > 0)
            .OrderByDescending(item => item.Amount)
            .ToList();

    public bool IsInternalMovement(Transaction t) =>
        IsGeneratedBalanceAdjustment(t) ||
        TransactionClassification.IsInternalMovementCategory(t.CategoryName) ||
        TransactionClassification.HasLinkedTransferId(t) ||
        TransactionClassification.IsCoverMovementDescription(t.Description) ||
        _matchedInternalMovementIds.Contains(t.Id);

    public bool IsBudgetedBillTransaction(Transaction t) =>
        IsBillCategory(t.CategoryName) ||
        IsBillLikeCategory(t.CategoryName) ||
        IsManuallyExcludedFromPace(t) ||
        MatchesKnownBudgetedPayment(t) ||
        MatchesBillRecord(t);

    public bool IsPaceCategoryExcluded(string? categoryName) =>
        !string.IsNullOrWhiteSpace(categoryName) && _paceExcludedCategories.Contains(categoryName.Trim());

    public bool IsPaceTransactionNameExcluded(string? transactionName) =>
        !string.IsNullOrWhiteSpace(transactionName) && _paceExcludedTransactionNames.Contains(transactionName.Trim());

    private bool IsManuallyExcludedFromPace(Transaction t) =>
        IsPaceCategoryExcluded(t.CategoryName) ||
        _paceExcludedTransactionNames.Any(name => TextContainsToken(t.Description, name));

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
            description.Contains("Suncorp Insurance", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("Insurance", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TextContainsToken(string text, string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        text.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> BuildPaceExclusionSet(IEnumerable<string>? values) =>
        values?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            var unnecessaryTxns = dayTxns?.Where(t => t.IsUnnecessary).ToList() ?? new List<Transaction>();
            var unnecessary = unnecessaryTxns.Sum(t => Math.Abs(t.AmountDollars));
            var transactionCount = dayTxns?.Count ?? 0;
            var unnecessaryCount = unnecessaryTxns.Count;
            var score = CalculateDailyScore(spending, unnecessary, transactionCount, unnecessaryCount);
            var explanation = BuildDailyScoreExplanation(spending, unnecessary, transactionCount, unnecessaryCount);
            var grade = spending == 0 ? "-" : score switch
            {
                100 => "A+", >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 50 => "D", _ => "F"
            };
            var color = spending == 0 ? "#6E7681" : score switch
            {
                100 => "#34D399", >= 80 => "#6EE7B7", >= 60 => "#FBBF24", >= 40 => "#F97316", _ => "#F87171"
            };
            result.Add(new DailyScore(date, spending, unnecessary, score, grade, color, explanation));
        }
        return result;
    }

    private static int CalculateDailyScore(decimal spending, decimal unnecessary, int transactionCount, int unnecessaryCount)
    {
        if (spending <= 0 || transactionCount <= 0) return 0;
        if (unnecessary <= 0 || unnecessaryCount <= 0) return 100;

        var unnecessarySpendRatio = (double)Math.Clamp(unnecessary / spending, 0m, 1m);
        var unnecessaryTxnRatio = Math.Clamp((double)unnecessaryCount / transactionCount, 0d, 1d);

        var score = 100
            - (unnecessarySpendRatio * 45)
            - (unnecessaryTxnRatio * 20)
            - Math.Min(unnecessaryCount * 5, 20);

        if (transactionCount <= 2) score += 15;
        else if (transactionCount <= 4) score += 8;

        if (unnecessary <= 25m) score += 15;
        else if (unnecessary <= 50m) score += 8;

        return Math.Clamp((int)Math.Round(score), 0, 100);
    }

    private static string BuildDailyScoreExplanation(decimal spending, decimal unnecessary, int transactionCount, int unnecessaryCount)
    {
        if (spending <= 0 || transactionCount <= 0) return "No spending recorded.";
        if (unnecessary <= 0 || unnecessaryCount <= 0)
            return $"{transactionCount} transaction{(transactionCount == 1 ? "" : "s")}, none marked unnecessary.";

        var spendShare = spending > 0 ? unnecessary / spending : 0m;
        var sizeLabel = unnecessary <= 25m
            ? "small amount"
            : unnecessary <= 50m ? "moderate amount" : "larger amount";
        var activityLabel = transactionCount <= 2
            ? "light spending day"
            : transactionCount <= 4 ? "normal spending day" : "busy spending day";

        return $"{unnecessaryCount} unnecessary transaction{(unnecessaryCount == 1 ? "" : "s")}, {unnecessary:C} ({spendShare:P0}) of {spending:C}; {sizeLabel}, {activityLabel}.";
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

    // Longest historical run of "spent something, none of it unnecessary"
    // days, scanning the full transaction history (not just the trailing
    // streak from GetCleanStreak).
    public int GetBestCleanStreak()
    {
        var txByDay = Transactions
            .Where(t => t.AmountCents < 0 && !IsInternalMovement(t))
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (txByDay.Count == 0) return 0;

        var best = 0;
        var current = 0;
        for (var date = txByDay.Keys.Min(); date <= DateTime.Today; date = date.AddDays(1))
        {
            if (txByDay.TryGetValue(date, out var dayTx) && dayTx.Count > 0 && !dayTx.Any(t => t.IsUnnecessary))
            {
                current++;
                best = Math.Max(best, current);
            }
            else
            {
                current = 0;
            }
        }
        return best;
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
    private static bool IsBillLikeCategory(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = name.Trim();
        return normalized.Contains("Insurance", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Rent", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Mortgage", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Loan", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Debt", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Utilities", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Electric", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Internet", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Phone", StringComparison.OrdinalIgnoreCase);
    }
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

    public async Task ToggleReimbursementAsync(int transactionId)
    {
        var t = Transactions.FirstOrDefault(x => x.Id == transactionId);
        if (t is null) return;
        t.IsReimbursement = !t.IsReimbursement;
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
        await db.SetBillEditOverrideAsync(b);
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
        if (b.Id > 0)
        {
            _pendingUpdatedBills.RemoveAll(x => x.Id == b.Id);
            _pendingUpdatedBills.Add(CloneBill(existing));
        }
        // Persist so a full bill edit survives an app restart (e.g. iOS
        // evicting a backgrounded PWA) before this push is confirmed —
        // without this the next sync pull silently reverts the edit.
        // ApplyPersistedBillEditOverridesAsync re-seeds the in-memory queue
        // from this on the next load.
        await db.SetBillEditOverrideAsync(existing);
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
        await db.ClearBillEditOverrideAsync(id);
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
        await db.SetDebtOverrideAsync(d);
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
            await QueueUpdatedDebt(existing);
        else
            await db.SetDebtOverrideAsync(existing);
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
            if (bill.Id > 0)
            {
                _pendingUpdatedBills.RemoveAll(x => x.Id == bill.Id);
                _pendingUpdatedBills.Add(CloneBill(bill));
            }
        }

        if (id > 0) _pendingDeletedDebtIds.Add(id);
        if (deletedDebt is not null && id > 0)
            await db.SetDebtDeleteAsync(deletedDebt);
        await db.DeleteAsync("debts", id);
        await db.ClearDebtOverrideAsync(id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task<SavingsGoal> AddSavingsGoalAsync(SavingsGoal g)
    {
        var minId = SavingsGoals.Count > 0 ? SavingsGoals.Min(x => x.Id) : 0;
        g.Id = Math.Min(minId - 1, -1);
        if (g.TargetDate is not null)
        {
            g.TargetStartDate = DateTime.Today;
            g.TargetStartingBalanceCents = g.CurrentCents;
        }
        SavingsGoals.Add(g);
        _pendingNewSavingsGoals.Add(g);
        await db.PutAsync("savingsGoals", g);
        await db.SetSavingsGoalOverrideAsync(g);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
        return g;
    }

    public async Task UpdateSavingsGoalAsync(SavingsGoal g)
    {
        var existing = SavingsGoals.FirstOrDefault(x => x.Id == g.Id);
        if (existing is null) return;
        LogGoal($"UPDATE before=[{GoalSnapshot(existing)}] after=[{GoalSnapshot(g)}]");
        var contributionCents = g.CurrentCents - existing.CurrentCents;
        existing.Name = g.Name;
        existing.TargetCents = g.TargetCents;
        existing.CurrentCents = g.CurrentCents;
        existing.WeeklyContributionCents = g.WeeklyContributionCents;
        existing.TargetDate = g.TargetDate;
        existing.GroupName = g.GroupName;
        existing.Emoji = g.Emoji;
        if (existing.TargetDate is not null && existing.TargetStartDate is null)
        {
            existing.TargetStartDate = DateTime.Today;
            existing.TargetStartingBalanceCents = existing.CurrentCents - contributionCents;
        }
        await db.PutAsync("savingsGoals", existing);
        // Queue this even for a not-yet-real-id (negative) goal. Once its
        // first create round-trip clears it from _pendingNewSavingsGoals,
        // relying on in-place object mutation alone stops working — the
        // Supabase canonical-store merge (BuildMergedCloudPayloadAsync) only
        // ever re-applies edits it finds in UpdatedSavingsGoals, matched by
        // Id, and that already-uploaded record keeps its negative Id forever
        // unless/until WPF later drains phone_push. Server-side handlers
        // already filter UpdatedSavingsGoals to Id > 0, so this is a no-op
        // for them and only feeds the canonical-store merge and the local
        // reapply-after-reload defense.
        await QueueUpdatedSavingsGoal(existing);
        if (contributionCents > 0)
            await RecordSavingsGoalContributionAsync(existing.Name, contributionCents);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    private const string SavingsGoalContributionCategoryName = "Savings Goal";

    // Money put toward a goal isn't a real bank transfer in this app (goals have
    // no linked account), so nothing ever reduced the weekly/monthly budget when
    // a contribution was made. Recording it as a normal expense transaction makes
    // it count toward GetDiscretionarySpendingForPeriod like any other spend, so
    // it visibly eats into safe-to-spend for the period it happened in.
    private async Task RecordSavingsGoalContributionAsync(string goalName, int contributionCents)
    {
        var category = Categories.FirstOrDefault(c => string.Equals(c.Name, SavingsGoalContributionCategoryName, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            var newCatId = Math.Min(Categories.Select(c => c.Id).DefaultIfEmpty(0).Min() - 1, -1);
            category = new Category { Id = newCatId, Name = SavingsGoalContributionCategoryName, Type = CategoryType.Expense };
            Categories.Add(category);
        }

        await AddTransactionAsync(new Transaction
        {
            Date = DateTime.Today,
            Description = $"Contribution to {goalName}",
            AmountCents = -contributionCents,
            CategoryId = category.Id,
            CategoryName = category.Name
        });
    }

    public async Task DeleteSavingsGoalAsync(int id)
    {
        var deletedGoal = SavingsGoals.FirstOrDefault(g => g.Id == id);
        if (deletedGoal is not null) LogGoal($"DELETE {GoalSnapshot(deletedGoal)}");
        SavingsGoals.RemoveAll(g => g.Id == id);
        _pendingNewSavingsGoals.RemoveAll(g => g.Id == id);
        _pendingUpdatedSavingsGoals.RemoveAll(g => g.Id == id);
        if (id > 0) _pendingDeletedSavingsGoalIds.Add(id);
        // Tombstone this even for a not-yet-synced (negative id) goal. A sync
        // round already in flight when the delete happens captured this goal
        // in its NewSavingsGoals snapshot before the delete; without a
        // tombstone, ReapplyPushChangesAsync's "re-add new goals the server
        // doesn't know about yet" pass has no way to tell the delete apart
        // from the pull having wiped out a pending create, and blindly
        // re-adds the goal that was just deleted.
        if (deletedGoal is not null)
            await db.SetSavingsGoalDeleteAsync(deletedGoal);
        await db.DeleteAsync("savingsGoals", id);
        await db.ClearSavingsGoalOverrideAsync(id);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public List<string> GetSavingsGoalGroupNames() =>
        SavingsGoals
            .Where(g => !string.IsNullOrWhiteSpace(g.GroupName))
            .Select(g => g.GroupName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

    public List<SavingsGoalGroup> GetSavingsGoalGroups() =>
        SavingsGoals
            .Where(g => !string.IsNullOrWhiteSpace(g.GroupName))
            .GroupBy(g => g.GroupName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(grp => new SavingsGoalGroup
            {
                Name = grp.First().GroupName!.Trim(),
                Goals = grp.OrderByDescending(g => g.CurrentDollars).ToList()
            })
            .OrderBy(grp => grp.Name)
            .ToList();

    public async Task<Trip> AddTripAsync(Trip t)
    {
        await _tripMutationGate.WaitAsync();
        try
        {
            var minId = Trips.Count > 0 ? Trips.Min(x => x.Id) : 0;
            t.Id = Math.Min(minId - 1, -1);
            Trips.Add(t);
            _pendingNewTrips.Add(t);
            await db.PutAsync("trips", t);
        }
        finally { _tripMutationGate.Release(); }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
        return t;
    }

    public async Task UpdateTripAsync(Trip t)
    {
        await _tripMutationGate.WaitAsync();
        try
        {
            var tripId = ResolveTripId(t.Id);
            var existing = Trips.FirstOrDefault(x => x.Id == tripId);
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
            if (existing.Id > 0)
                await QueueUpdatedTripAsync(existing);
            else
                await QueueNewTripEditAsync(existing);
        }
        finally { _tripMutationGate.Release(); }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task SaveTripSnapshotAsync(Trip t)
    {
        await _tripMutationGate.WaitAsync();
        try
        {
            var tripId = ResolveTripId(t.Id);
            var existing = Trips.FirstOrDefault(x => x.Id == tripId);
            if (existing is null) return;
            CopyTripFields(t, existing);
            await db.PutAsync("trips", existing);
            if (existing.Id > 0)
                await QueueUpdatedTripAsync(existing);
            else
                await QueueNewTripEditAsync(existing);
        }
        finally { _tripMutationGate.Release(); }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public async Task DeleteTripAsync(int id)
    {
        await _tripMutationGate.WaitAsync();
        try
        {
            id = ResolveTripId(id);
            var deletedTrip = Trips.FirstOrDefault(t => t.Id == id);
            Trips.RemoveAll(t => t.Id == id);
            _pendingNewTrips.RemoveAll(t => t.Id == id);
            _pendingUpdatedTrips.RemoveAll(t => t.Id == id);
            if (id > 0) _pendingDeletedTripIds.Add(id);
            if (deletedTrip is not null && id > 0)
                await db.SetTripDeleteAsync(deletedTrip);
            await db.DeleteAsync("trips", id);
            await db.ClearTripOverrideAsync(id);
        }
        finally { _tripMutationGate.Release(); }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    // Targeted Budget/Itinerary/Checklist item mutators. These always take plain
    // values (never a whole Trip snapshot from a caller) and do a fresh, gated
    // lookup of the live Trip right before mutating just the one targeted item.
    // This closes the race that UpdateTripAsync(trip) was vulnerable to: a Razor
    // component reading SelectedTrip, mutating one item in place, then handing
    // the whole Trip back to UpdateTripAsync — if that read happened in the gap
    // between LoadAsync()'s wholesale replace and the reapply's correction, the
    // component's clone of the trip carried stale sibling items that could win
    // a later replay and silently revert an already-synced edit.
    private async Task MutateTripAsync(int tripId, Action<Trip> mutate)
    {
        await _tripMutationGate.WaitAsync();
        try
        {
            tripId = ResolveTripId(tripId);
            var trip = Trips.FirstOrDefault(x => x.Id == tripId);
            if (trip is null) return;
            LogTrip($"MUTATE trip={tripId} before {ItinSnapshot(trip)}");
            mutate(trip);
            LogTrip($"MUTATE trip={tripId} after {ItinSnapshot(trip)}");
            await db.PutAsync("trips", trip);
            if (trip.Id > 0)
                await QueueUpdatedTripAsync(trip);
            else
                await QueueNewTripEditAsync(trip);
        }
        finally { _tripMutationGate.Release(); }
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public Task AddBudgetItemAsync(int tripId, TripBudgetItem newItem) =>
        MutateTripAsync(tripId, trip => trip.BudgetItems.Add(newItem));

    public Task SaveBudgetItemAsync(int tripId, string itemId, string category, decimal plannedDollars, decimal actualDollars, bool paid, string? notes) =>
        MutateTripAsync(tripId, trip =>
        {
            var item = trip.BudgetItems.FirstOrDefault(b => b.Id == itemId);
            if (item is null) return;
            item.Category = category;
            item.PlannedDollars = plannedDollars;
            item.ActualDollars = actualDollars;
            item.ActualEntered = true;
            item.Paid = paid;
            item.Notes = notes;
        });

    public Task SetBudgetPaidAsync(int tripId, string itemId, bool paid) =>
        MutateTripAsync(tripId, trip =>
        {
            var item = trip.BudgetItems.FirstOrDefault(b => b.Id == itemId);
            if (item is null) return;
            item.Paid = paid;
            if (!item.Paid)
            {
                item.ActualDollars = 0;
                item.ActualEntered = false;
            }
        });

    public Task RemoveBudgetItemAsync(int tripId, string itemId) =>
        MutateTripAsync(tripId, trip =>
        {
            trip.BudgetItems.RemoveAll(b => b.Id == itemId);
            // Orphan rather than delete linked schedule entries — the activity
            // and its amount are still real, only the budget category backing it
            // is gone, so fall back to a manually-tracked amount.
            foreach (var i in trip.Itinerary.Where(i => i.BudgetItemId == itemId))
                i.BudgetItemId = null;
        });

    public Task AddItineraryItemAsync(int tripId, TripItineraryItem newItem) =>
        MutateTripAsync(tripId, trip => trip.Itinerary.Add(newItem));

    public Task SaveItineraryItemAsync(int tripId, string itemId, DateTime date, string? time, string? endTime, string title, decimal amountDollars, string? notes, string? budgetItemId) =>
        MutateTripAsync(tripId, trip =>
        {
            var item = trip.Itinerary.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return;
            item.Date = date;
            item.Time = time;
            item.EndTime = endTime;
            item.Title = title;
            item.AmountDollars = amountDollars;
            item.Notes = notes;
            item.BudgetItemId = budgetItemId;
        });

    // Sum of itinerary amounts currently allocated against a budget item —
    // lets the UI show "$X allocated of $Y planned" instead of the schedule
    // and budget totals silently drifting apart from independent manual entry.
    public decimal GetAllocatedDollars(Trip trip, string budgetItemId) =>
        trip.Itinerary.Where(i => i.BudgetItemId == budgetItemId).Sum(i => i.AmountDollars);

    public Task RemoveItineraryItemAsync(int tripId, string itemId) =>
        MutateTripAsync(tripId, trip => trip.Itinerary.RemoveAll(i => i.Id == itemId));

    public Task ApplyChecklistTemplateAsync(int tripId, string[] items) =>
        MutateTripAsync(tripId, trip =>
        {
            var existing = new HashSet<string>(trip.Checklist.Select(c => c.Text), StringComparer.OrdinalIgnoreCase);
            foreach (var text in items)
            {
                if (existing.Contains(text)) continue;
                trip.Checklist.Add(new TripChecklistItem { Text = text, Done = false });
            }
        });

    public Task AddChecklistItemAsync(int tripId, TripChecklistItem newItem) =>
        MutateTripAsync(tripId, trip => trip.Checklist.Add(newItem));

    public Task SaveChecklistItemAsync(int tripId, string itemId, string text, bool done, DateTime? dueDate) =>
        MutateTripAsync(tripId, trip =>
        {
            var item = trip.Checklist.FirstOrDefault(c => c.Id == itemId);
            if (item is null) return;
            item.Text = text;
            item.Done = done;
            item.DueDate = dueDate;
        });

    public Task RemoveChecklistItemAsync(int tripId, string itemId) =>
        MutateTripAsync(tripId, trip => trip.Checklist.RemoveAll(c => c.Id == itemId));

    public Task ToggleChecklistItemAsync(int tripId, string itemId, bool done) =>
        MutateTripAsync(tripId, trip =>
        {
            var item = trip.Checklist.FirstOrDefault(c => c.Id == itemId);
            if (item is null) return;
            item.Done = done;
        });

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
        await QueueUpdatedAccount(account);
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
        await QueueUpdatedAccount(account);
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
        await QueueUpdatedAccount(account);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    private async Task QueueUpdatedAccount(Account a)
    {
        _pendingUpdatedAccounts.RemoveAll(x => x.Id == a.Id);
        _pendingUpdatedAccounts.Add(CloneAccount(a));
        // Persist so an account goal edit survives an app restart (e.g. iOS
        // evicting a backgrounded PWA) before this push is confirmed —
        // without this the next sync pull silently reverts the edit.
        // ApplyPersistedAccountOverridesAsync re-seeds the in-memory queue
        // from this on the next load.
        await db.SetAccountOverrideAsync(a);
    }

    private async Task QueueUpdatedDebt(Debt d)
    {
        _pendingUpdatedDebts.RemoveAll(x => x.Id == d.Id);
        _pendingUpdatedDebts.Add(CloneDebt(d));
        await db.SetDebtOverrideAsync(d);
    }

    private async Task QueueUpdatedSavingsGoal(SavingsGoal g)
    {
        _pendingUpdatedSavingsGoals.RemoveAll(x => x.Id == g.Id);
        _pendingUpdatedSavingsGoals.Add(CloneSavingsGoal(g));
        await db.SetSavingsGoalOverrideAsync(g);
    }

    private async Task QueueUpdatedTripAsync(Trip t)
    {
        _pendingUpdatedTrips.RemoveAll(x => x.Id == t.Id);
        // Snapshot a clone, not the live object — t keeps mutating in place as the
        // user makes further edits, and an in-flight push payload (built earlier from
        // this same list) must not silently pick up changes made after it was sent.
        var clone = CloneTrip(t);
        _pendingUpdatedTrips.Add(clone);
        LogTrip($"QUEUE trip={t.Id} {ItinSnapshot(clone)}");

        // Persist the clone too — _pendingUpdatedTrips is in-memory only and is
        // lost if the WASM runtime restarts (e.g. iOS evicting a backgrounded
        // PWA) before this edit is pushed. Without this, the next sync pulls
        // the still-stale server copy with nothing left to defend it, silently
        // reverting the edit. ApplyPersistedTripOverridesAsync re-seeds the
        // in-memory queue from this on the next load.
        await db.SetTripOverrideAsync(clone);
    }

    // Mirrors QueueUpdatedTripAsync for trips still stuck at a negative
    // (not-yet-adopted) id. _pendingNewTrips previously relied on the live
    // Trip object reference staying queued, which is silently orphaned the
    // moment LoadAsync() wholesale-replaces Trips with freshly deserialized
    // objects — losing the edit with no override to restore it on reload.
    private async Task QueueNewTripEditAsync(Trip t)
    {
        _pendingNewTrips.RemoveAll(x => x.Id == t.Id);
        var clone = CloneTrip(t);
        _pendingNewTrips.Add(clone);
        LogTrip($"QUEUE-NEW trip={t.Id} {ItinSnapshot(clone)}");
        await db.SetTripOverrideAsync(clone);
    }

    private int ResolveTripId(int tripId)
    {
        while (tripId < 0 && _adoptedTripIds.TryGetValue(tripId, out var adoptedId))
            tripId = adoptedId;
        return tripId;
    }

    private void AdoptTripId(int oldId, int newId)
    {
        if (oldId == newId || oldId > 0 || newId <= 0) return;
        _adoptedTripIds[oldId] = newId;
        OnTripIdAdopted?.Invoke(oldId, newId);
    }

    private void AdoptPulledTripIds(List<Trip> negativeTripsBeforeLoad)
    {
        foreach (var local in negativeTripsBeforeLoad)
        {
            var adopted = Trips.FirstOrDefault(t => SameTripAdoptionCandidate(t, local));
            if (adopted is not null)
                AdoptTripId(local.Id, adopted.Id);
        }
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

    // Snapshot clones for the pending-update queues. Edit methods mutate the
    // live Bills/Debts/Accounts/SavingsGoals list entry in place (the UI binds
    // directly to that same object) — if the *same* mutable reference were
    // queued into _pendingUpdatedX, a second edit landing while an earlier
    // push for that id is still in flight would silently rewrite the
    // in-flight snapshot too. When that push then completes, the
    // reference-based removal block in SyncAndReloadAsync would clear the
    // pending entry/override for the id entirely — even though the second
    // edit's value was never actually sent over the wire — leaving it with no
    // defense against the next pull. Queuing an independent clone each time
    // means a later edit's RemoveAll+Add always swaps in a genuinely different
    // object, so removing the exact instance that was pushed never removes a
    // newer, not-yet-sent edit for the same id.
    private static Account CloneAccount(Account a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        UpAccountId = a.UpAccountId,
        Type = a.Type,
        ColorHex = a.ColorHex,
        TargetCents = a.TargetCents,
        TargetDate = a.TargetDate,
        TargetStartDate = a.TargetStartDate,
        TargetStartingBalanceCents = a.TargetStartingBalanceCents
    };

    private static Debt CloneDebt(Debt d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        BalanceCents = d.BalanceCents,
        MinimumPaymentCents = d.MinimumPaymentCents,
        PaymentPeriod = d.PaymentPeriod,
        InterestRate = d.InterestRate,
        OriginalBalanceCents = d.OriginalBalanceCents,
        UpPaymentMatchText = d.UpPaymentMatchText
    };

    private static Bill CloneBill(Bill b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        AccountId = b.AccountId,
        DebtId = b.DebtId,
        AmountCents = b.AmountCents,
        DueDate = b.DueDate,
        NextPayDate = b.NextPayDate,
        Frequency = b.Frequency,
        IsPaid = b.IsPaid,
        IsCreatedFromRecurringPayment = b.IsCreatedFromRecurringPayment,
        IsAutoPay = b.IsAutoPay,
        PaymentMatchText = b.PaymentMatchText,
        AccountName = b.AccountName
    };

    private static SavingsGoal CloneSavingsGoal(SavingsGoal g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        TargetCents = g.TargetCents,
        CurrentCents = g.CurrentCents,
        WeeklyContributionCents = g.WeeklyContributionCents,
        TargetDate = g.TargetDate,
        GroupName = g.GroupName,
        TargetStartDate = g.TargetStartDate,
        TargetStartingBalanceCents = g.TargetStartingBalanceCents,
        Emoji = g.Emoji
    };

    private static Trip CloneTrip(Trip t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Destination = t.Destination,
        Notes = t.Notes,
        StartDate = t.StartDate,
        EndDate = t.EndDate,
        SavingsAccountId = t.SavingsAccountId,
        WeeklyContributionCents = t.WeeklyContributionCents,
        Itinerary = t.Itinerary.Select(i => new TripItineraryItem
        {
            Id = i.Id,
            Date = i.Date,
            Time = i.Time,
            EndTime = i.EndTime,
            Title = i.Title,
            Notes = i.Notes,
            AmountCents = i.AmountCents
        }).ToList(),
        Checklist = t.Checklist.Select(c => new TripChecklistItem
        {
            Id = c.Id,
            Text = c.Text,
            Done = c.Done,
            DueDate = c.DueDate
        }).ToList(),
        BudgetItems = t.BudgetItems.Select(b => new TripBudgetItem
        {
            Id = b.Id,
            Category = b.Category,
            PlannedCents = b.PlannedCents,
            ActualCents = b.ActualCents,
            ActualEntered = b.ActualEntered,
            Paid = b.Paid,
            Notes = b.Notes
        }).ToList()
    };

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
                await QueueUpdatedDebt(existingDebt);
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
        await QueueUpdatedDebt(debt);

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
        var category = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null) return;

        // Only this transaction changes here — bulk-renaming every transaction that
        // shares this description is reserved for the explicit "apply to all
        // matching names" action below, not an implicit side effect of one edit.
        t.CategoryId = category.Id;
        t.CategoryName = category.Name;
        await db.PutAsync("transactions", t);
        QueueUpdatedTransaction(t);
        await db.SetTransactionOverrideAsync(t);
        Compute();
        OnChange?.Invoke();
        ScheduleSyncSoon();
    }

    public int GetSameNameTransactionCount(Transaction transaction)
    {
        var normalized = NormalizeRecurringDescription(transaction.Description);
        return Transactions.Count(t =>
            t.AmountCents < 0 &&
            !IsInternalMovement(t) &&
            string.Equals(NormalizeRecurringDescription(t.Description), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private TransactionCategoryRules LoadTransactionCategoryRules()
    {
        var rules = GetSettingJson<TransactionCategoryRules>(TransactionCategoryRulesSettingKey) ?? new TransactionCategoryRules();
        return NormalizeTransactionCategoryRules(rules);
    }

    private static TransactionCategoryRules NormalizeTransactionCategoryRules(TransactionCategoryRules rules)
    {
        rules.Rules = rules.Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.NormalizedName) && !string.IsNullOrWhiteSpace(r.CategoryName))
            .GroupBy(r => r.NormalizedName.Trim().ToUpperInvariant())
            .Select(g => g.Last())
            .ToList();
        return rules;
    }

    private async Task SaveTransactionCategoryRuleAsync(Transaction transaction, Category category)
    {
        var normalized = NormalizeRecurringDescription(transaction.Description);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        _transactionCategoryRules = LoadTransactionCategoryRules();
        _transactionCategoryRules.Rules.RemoveAll(r =>
            string.Equals(r.NormalizedName, normalized, StringComparison.OrdinalIgnoreCase));
        _transactionCategoryRules.Rules.Add(new TransactionCategoryRule
        {
            NormalizedName = normalized,
            CategoryId = category.Id,
            CategoryName = category.Name
        });
        _transactionCategoryRules = NormalizeTransactionCategoryRules(_transactionCategoryRules);
        await SaveSettingAsync(TransactionCategoryRulesSettingKey, System.Text.Json.JsonSerializer.Serialize(_transactionCategoryRules));
    }

    private async Task ApplyTransactionCategoryRulesAsync(bool persistTransactionOverrides)
    {
        _transactionCategoryRules = LoadTransactionCategoryRules();
        foreach (var rule in _transactionCategoryRules.Rules)
        {
            var category = FindCategory(rule.CategoryName, CategoryType.Expense)
                ?? Categories.FirstOrDefault(c => c.Id == rule.CategoryId)
                ?? Categories.FirstOrDefault(c => string.Equals(c.Name, rule.CategoryName, StringComparison.OrdinalIgnoreCase));
            if (category is null) continue;

            await ApplyTransactionCategoryRuleAsync(rule.NormalizedName, category, persistTransactionOverrides);
        }
    }

    private async Task<int> ApplyTransactionCategoryRuleAsync(string normalizedName, Category category, bool persistTransactionOverrides)
    {
        if (string.IsNullOrWhiteSpace(normalizedName)) return 0;

        var matches = Transactions
            .Where(t => t.AmountCents < 0 &&
                !IsInternalMovement(t) &&
                string.Equals(NormalizeRecurringDescription(t.Description), normalizedName, StringComparison.OrdinalIgnoreCase) &&
                (t.CategoryId != category.Id || !string.Equals(t.CategoryName, category.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var transaction in matches)
        {
            transaction.CategoryId = category.Id;
            transaction.CategoryName = category.Name;
            await db.PutAsync("transactions", transaction);
            if (persistTransactionOverrides)
            {
                QueueUpdatedTransaction(transaction);
                await db.SetTransactionOverrideAsync(transaction);
            }
        }

        return matches.Count;
    }

    public async Task<int> UpdateSameNameTransactionCategoriesAsync(int transactionId, int categoryId)
    {
        var seed = Transactions.FirstOrDefault(t => t.Id == transactionId);
        var category = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (seed is null || category is null) return 0;

        var normalized = NormalizeRecurringDescription(seed.Description);
        await SaveTransactionCategoryRuleAsync(seed, category);
        var matches = Transactions
            .Where(t => t.AmountCents < 0 &&
                !IsInternalMovement(t) &&
                string.Equals(NormalizeRecurringDescription(t.Description), normalized, StringComparison.OrdinalIgnoreCase) &&
                t.CategoryId != categoryId)
            .ToList();

        foreach (var transaction in matches)
        {
            transaction.CategoryId = category.Id;
            transaction.CategoryName = category.Name;
            await db.PutAsync("transactions", transaction);
            QueueUpdatedTransaction(transaction);
            await db.SetTransactionOverrideAsync(transaction);
        }

        if (matches.Count > 0)
        {
            Compute();
            OnChange?.Invoke();
            ScheduleSyncSoon();
        }

        return matches.Count;
    }

    public int GetCategoryUsageCount(int categoryId) =>
        Transactions.Count(t => t.CategoryId == categoryId);

    private CategoryManagementRules LoadCategoryManagementRules()
    {
        var rules = GetSettingJson<CategoryManagementRules>(CategoryManagementRulesSettingKey) ?? new CategoryManagementRules();
        return NormalizeCategoryManagementRules(rules);
    }

    private static CategoryManagementRules NormalizeCategoryManagementRules(CategoryManagementRules rules)
    {
        rules.AddedCategories = rules.AddedCategories
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => $"{r.Type}:{r.Name.Trim().ToUpperInvariant()}")
            .Select(g => g.Last())
            .ToList();
        rules.DeletedCategories = rules.DeletedCategories
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.ReplacementName))
            .GroupBy(r => $"{r.Type}:{r.Name.Trim().ToUpperInvariant()}")
            .Select(g => g.Last())
            .ToList();
        return rules;
    }

    private async Task SaveCategoryManagementRulesAsync()
    {
        _categoryManagementRules = NormalizeCategoryManagementRules(_categoryManagementRules);
        await SaveSettingAsync(CategoryManagementRulesSettingKey, System.Text.Json.JsonSerializer.Serialize(_categoryManagementRules));
    }

    private Category? FindCategory(string name, CategoryType type) =>
        Categories.FirstOrDefault(c =>
            c.Type == type &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private int NextLocalCategoryId()
    {
        var min = Categories.Select(c => c.Id).DefaultIfEmpty(0).Min();
        return Math.Min(min - 1, -1);
    }

    private async Task ApplyManagedCategoryRulesAsync(bool persistTransactionOverrides)
    {
        _categoryManagementRules = LoadCategoryManagementRules();
        if (_categoryManagementRules.AddedCategories.Count == 0 && _categoryManagementRules.DeletedCategories.Count == 0)
            return;

        var changed = false;
        foreach (var rule in _categoryManagementRules.AddedCategories)
        {
            if (_categoryManagementRules.DeletedCategories.Any(d =>
                d.Type == rule.Type &&
                string.Equals(d.Name, rule.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (FindCategory(rule.Name, rule.Type) is not null) continue;
            var id = rule.Id < 0 && Categories.All(c => c.Id != rule.Id) ? rule.Id : NextLocalCategoryId();
            var category = new Category { Id = id, Name = rule.Name.Trim(), Type = rule.Type };
            Categories.Add(category);
            await db.PutAsync("categories", category);
            changed = true;
        }

        foreach (var rule in _categoryManagementRules.DeletedCategories)
        {
            var replacement = FindCategory(rule.ReplacementName, rule.Type)
                ?? Categories.FirstOrDefault(c => c.Id == rule.ReplacementId && c.Type == rule.Type)
                ?? Categories.FirstOrDefault(c => c.Type == rule.Type && !string.Equals(c.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
            if (replacement is null) continue;

            var deletedIds = Categories
                .Where(c => c.Type == rule.Type &&
                    (c.Id == rule.Id || string.Equals(c.Name, rule.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Id)
                .ToHashSet();

            var affected = Transactions
                .Where(t => deletedIds.Contains(t.CategoryId) ||
                    string.Equals(t.CategoryName, rule.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var transaction in affected)
            {
                if (transaction.CategoryId == replacement.Id &&
                    string.Equals(transaction.CategoryName, replacement.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                transaction.CategoryId = replacement.Id;
                transaction.CategoryName = replacement.Name;
                await db.PutAsync("transactions", transaction);
                if (persistTransactionOverrides)
                {
                    QueueUpdatedTransaction(transaction);
                    await db.SetTransactionOverrideAsync(transaction);
                }
                changed = true;
            }

            var removed = Categories.RemoveAll(c => deletedIds.Contains(c.Id));
            if (removed > 0)
            {
                foreach (var id in deletedIds)
                    await db.DeleteAsync("categories", id);
                changed = true;
            }

            if (_paceExcludedCategories.Remove(rule.Name))
            {
                var value = System.Text.Json.JsonSerializer.Serialize(_paceExcludedCategories.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
                if (persistTransactionOverrides)
                    await SaveSettingAsync(PaceExcludedCategoriesSettingKey, value);
                else
                {
                    await db.SaveSettingAsync(PaceExcludedCategoriesSettingKey, value);
                    var existing = AppSettings.FirstOrDefault(s => s.Key == PaceExcludedCategoriesSettingKey);
                    if (existing is not null) existing.Value = value;
                    else AppSettings.Add(new AppSetting { Key = PaceExcludedCategoriesSettingKey, Value = value });
                }
            }
        }

        if (changed)
        {
            if (persistTransactionOverrides) ScheduleSyncSoon();
        }
    }

    public async Task<(bool Ok, string Message)> AddCategoryAsync(string name, CategoryType type)
    {
        var clean = name.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return (false, "Category name is required.");
        if (Categories.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return (false, "That category already exists.");

        var id = NextLocalCategoryId();
        var category = new Category { Id = id, Name = clean, Type = type };
        Categories.Add(category);
        await db.PutAsync("categories", category);
        _categoryManagementRules = LoadCategoryManagementRules();
        _categoryManagementRules.DeletedCategories.RemoveAll(r =>
            r.Type == type && string.Equals(r.Name, clean, StringComparison.OrdinalIgnoreCase));
        _categoryManagementRules.AddedCategories.RemoveAll(r =>
            r.Type == type && string.Equals(r.Name, clean, StringComparison.OrdinalIgnoreCase));
        _categoryManagementRules.AddedCategories.Add(new ManagedCategoryRule { Id = id, Name = clean, Type = type });
        await SaveCategoryManagementRulesAsync();
        return (true, $"Added {clean}.");
    }

    public async Task<(bool Ok, string Message)> DeleteCategoryAsync(int categoryId, int replacementCategoryId)
    {
        var category = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null) return (false, "Category not found.");
        if (categoryId == replacementCategoryId) return (false, "Pick a different replacement category.");

        var replacement = Categories.FirstOrDefault(c => c.Id == replacementCategoryId);
        if (replacement is null) return (false, "Replacement category not found.");
        if (replacement.Type != category.Type) return (false, "Replacement must be the same type.");

        var affected = Transactions.Where(t => t.CategoryId == categoryId).ToList();
        foreach (var transaction in affected)
        {
            transaction.CategoryId = replacement.Id;
            transaction.CategoryName = replacement.Name;
            await db.PutAsync("transactions", transaction);
            QueueUpdatedTransaction(transaction);
            await db.SetTransactionOverrideAsync(transaction);
        }

        Categories.RemoveAll(c => c.Id == categoryId);
        await db.DeleteAsync("categories", categoryId);
        _categoryManagementRules = LoadCategoryManagementRules();
        var wasLocalAddition = _categoryManagementRules.AddedCategories.RemoveAll(r =>
            r.Type == category.Type && string.Equals(r.Name, category.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        _categoryManagementRules.DeletedCategories.RemoveAll(r =>
            r.Type == category.Type && string.Equals(r.Name, category.Name, StringComparison.OrdinalIgnoreCase));
        if (!wasLocalAddition)
        {
            _categoryManagementRules.DeletedCategories.Add(new DeletedCategoryRule
            {
                Id = category.Id,
                Name = category.Name,
                Type = category.Type,
                ReplacementId = replacement.Id,
                ReplacementName = replacement.Name
            });
        }
        _paceExcludedCategories.Remove(category.Name);
        await SaveSettingAsync(PaceExcludedCategoriesSettingKey, System.Text.Json.JsonSerializer.Serialize(_paceExcludedCategories.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)));
        await SaveCategoryManagementRulesAsync();

        Compute();
        OnChange?.Invoke();
        if (affected.Count > 0) ScheduleSyncSoon();
        return (true, $"Deleted {category.Name}; moved {affected.Count} transaction{(affected.Count == 1 ? "" : "s")} to {replacement.Name}. Sync will keep it deleted.");
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

    public Task MarkBillPaidAsync(int billId, bool paid)
    {
        var bill = Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null) return Task.CompletedTask;
        var effectiveDue = bill.EffectiveDueDate == default ? GetEffectiveDueDate(bill) : bill.EffectiveDueDate;
        return MarkBillOccurrencePaidAsync(billId, effectiveDue, paid);
    }

    // Marks a SPECIFIC occurrence paid/unpaid, rather than always the bill's
    // current cycle — needed so an overdue/missed occurrence surfaced by
    // GetOutstandingBillOccurrences can be settled against its own due date
    // instead of getting recorded against today's date.
    public async Task MarkBillOccurrencePaidAsync(int billId, DateTime dueDate, bool paid)
    {
        var bill = Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null) return;

        var status = BillStatuses.FirstOrDefault(s => s.BillId == billId && s.DueDate.Date == dueDate.Date);
        if (status is null)
        {
            status = new BillOccurrenceStatus { Id = NextLocalId(BillStatuses.Select(s => s.Id)), BillId = billId, DueDate = dueDate.Date };
            BillStatuses.Add(status);
        }
        status.IsPaid = paid;
        status.PaidOn = paid ? DateTime.Now : null;

        _pendingBillStatuses.RemoveAll(s => s.BillId == status.BillId && s.DueDate.Date == status.DueDate.Date);
        _pendingBillStatuses.Add(status);
        await db.PutAsync("billOccurrenceStatuses", status);

        // The bill-level IsPaid flag and its sync override only make sense for
        // the CURRENT cycle — don't let settling an old missed week flip them.
        var effectiveDue = bill.EffectiveDueDate == default ? GetEffectiveDueDate(bill) : bill.EffectiveDueDate;
        if (dueDate.Date == effectiveDue.Date)
        {
            bill.IsPaid = paid;
            await db.PutAsync("bills", bill);
            // Persist the override so it survives sync's clearAll and app restarts
            await db.SetBillOverrideAsync(billId, paid);
        }

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
        try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
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
        target.IsReimbursement = updated.IsReimbursement;
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
        IsUnnecessary = transaction.IsUnnecessary,
        IsReimbursement = transaction.IsReimbursement
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

    private static bool SameBillSnapshot(Bill left, Bill right)
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
        // Two distinct goals can share the same name and amounts (e.g. two
        // goals both called "Bed" saved toward the same target) but belong to
        // different groups. Without this check the fallback above would treat
        // them as the same goal and delete/resurrect the wrong one.
        if (!string.Equals((goal.GroupName ?? "").Trim(), (deleted.GroupName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return false;
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
        var current = AppSettings.FirstOrDefault(s => s.Key == key);
        if (current is not null && string.Equals(current.Value, value, StringComparison.Ordinal))
            return;

        await db.SaveSettingAsync(key, value);
        AppSettings = await db.GetAppSettingsAsync();
        var setting = AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting is not null && ShouldSyncSetting(key))
        {
            _pendingUpdatedSettings.RemoveAll(s => s.Key == key);
            _pendingUpdatedSettings.Add(setting);
            // Persisted separately from the in-memory queue so a setting change
            // (e.g. payday) survives an app restart that cuts off the debounced
            // push, instead of silently being lost and reverted by the next pull.
            await db.SetSettingOverrideAsync(setting);
        }
        else
        {
            _pendingUpdatedSettings.RemoveAll(s => s.Key == key);
            await db.ClearSettingOverrideAsync(key);
        }
        Compute();
        OnChange?.Invoke();
        if (ShouldSyncSetting(key)) ScheduleSyncSoon();
    }

    public async Task SetSpendingPaceCategoryExcludedAsync(string categoryName, bool excluded)
    {
        await SetSpendingPaceExclusionAsync(PaceExcludedCategoriesSettingKey, _paceExcludedCategories, categoryName, excluded);
    }

    public async Task SetSpendingPaceTransactionNameExcludedAsync(string transactionName, bool excluded)
    {
        await SetSpendingPaceExclusionAsync(PaceExcludedTransactionNamesSettingKey, _paceExcludedTransactionNames, transactionName, excluded);
    }

    private async Task SetSpendingPaceExclusionAsync(string key, HashSet<string> target, string value, bool excluded)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var clean = value.Trim();
        if (excluded) target.Add(clean);
        else target.Remove(clean);

        var ordered = target.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        await SaveSettingAsync(key, System.Text.Json.JsonSerializer.Serialize(ordered));
    }

    public async Task SetNoSpendModeAsync(bool enabled)
    {
        await SaveSettingAsync("NoSpendMode", enabled ? "true" : "false");
        if (enabled && NoSpendModeSince is null)
            await SaveSettingAsync("NoSpendModeSince", DateTime.Today.ToString("O"));
        else if (!enabled)
            await SaveSettingAsync("NoSpendModeSince", string.Empty);
    }

    private static bool ShouldSyncSetting(string key) =>
        key is not ("AffordabilityMode"
            or "AffordabilityInstallmentAmount"
            or "AffordabilityInstallments"
            or "AffordabilityInstallmentWeeks"
            or "VapidPublicKey"
            or AppLockPinHashKey) &&
        !key.StartsWith("CategoryLimit:", StringComparison.Ordinal);

    // Phone-editable weekly budget targets — field is one of Income/Bills/Essentials/
    // Savings/Unplanned. Saved as a setting override so it survives the next sync
    // pull instead of being reverted by the desktop's WeeklyBudget record.
    public async Task SetBudgetOverrideAsync(string field, string? raw)
    {
        if (!decimal.TryParse(raw, out var dollars) || dollars < 0) dollars = 0;
        await SaveSettingAsync($"WeeklyBudgetOverride:{field}", dollars.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            existing.IsReimbursement = u.IsReimbursement;
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
            existing.IsReimbursement = edit.IsReimbursement;
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
            if (existing is null) { cloud.Bills.Add(u); continue; }
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
            else
            {
                // Debt not found in cloud snapshot — cloud may be sparse or
                // the debt was never pushed. Add it so the edit isn't lost.
                cloud.Debts.Add(u);
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
                LogGoal($"MERGE-UPDATE cloud=[{GoalSnapshot(existing)}] incoming=[{GoalSnapshot(u)}]");
                existing.Name = u.Name;
                existing.TargetCents = u.TargetCents;
                existing.CurrentCents = u.CurrentCents;
                existing.WeeklyContributionCents = u.WeeklyContributionCents;
                existing.TargetDate = u.TargetDate;
                existing.GroupName = u.GroupName;
            }
        }
        // AddRange only for goals the canonical snapshot doesn't already have —
        // a not-yet-real-id goal stays at the same negative Id in cloud.SavingsGoals
        // across every future round (nothing here ever reconciles it to a real Id
        // unless/until WPF drains phone_push), so a blind AddRange would duplicate
        // it the moment it's ever re-queued for any reason.
        var cloudGoalIds = cloud.SavingsGoals.Select(g => g.Id).ToHashSet();
        cloud.SavingsGoals.AddRange(push.NewSavingsGoals.Where(g => !cloudGoalIds.Contains(g.Id)));

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
        foreach (var t in push.NewTrips)
        {
            // A trip stuck at the same negative Id (never adopted) is pushed as
            // "new" again on every round once it has further edits — update the
            // existing canonical entry in place instead of skipping it, or the
            // cloud snapshot freezes at whatever it looked like on the very first
            // push and every later edit silently reverts on the next pull.
            var existingSameId = cloud.Trips.FirstOrDefault(x => x.Id == t.Id);
            if (existingSameId is not null)
            {
                CopyTripFields(t, existingSameId);
                continue;
            }

            var adopted = cloud.Trips.FirstOrDefault(x => SameTripAdoptionCandidate(x, t));
            if (adopted is not null)
            {
                CopyTripFields(t, adopted);
                continue;
            }

            cloud.Trips.Add(t);
        }

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
        OnChange?.Invoke();
        LogTrip($"SYNC-START pendingTrips=[{string.Join(" || ", _pendingUpdatedTrips.Select(t => ItinSnapshot(t)))}]");
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
            LastSyncPushStatus = null;

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
                foreach (var t in push.UpdatedTrips)
                    LogTrip($"PUSH-BUILD trip={t.Id} {ItinSnapshot(t)}");

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
                // received it — both give WPF a path to reconcile. sentPush drives the
                // in-memory ReapplyPushChangesAsync defense below for this session, which
                // we want even when the canonical write fails (see pushedToCanonicalStore).
                var pushed = pushedToCloud || pushedToPc;
                if (pushed)
                    sentPush = push;

                // Record push outcome for SyncNowAsync to show a meaningful status.
                if (!pushed)
                    LastSyncPushStatus = sync.LastPushError ?? $"push not accepted (cloud={sync.HasCloudSync}, pc={sync.HasLocalSync})";
                else if (!pushedToCanonicalStore)
                    LastSyncPushStatus = sync.LastPushError ?? "finance_sync not updated (merged write failed)";
                else
                    LastSyncPushStatus = null;

                // Only clear persisted overrides/tombstones and drop entries from the
                // pending queues once finance_sync — the snapshot AutoSyncAsync actually
                // pulls from — has genuinely been updated. phone_push being accepted is
                // not enough: PushFullSyncAsync's merge is the thing that determines what
                // the next pull sees, and it can fail (or, if HasCloudSync is false here,
                // pushedToCanonicalStore already equals pushedToPc). Clearing on phone_push
                // acceptance alone deletes the only on-disk record of an edit before the
                // pull that depends on it can possibly reflect it — the in-memory
                // ReapplyPushChangesAsync defense doesn't survive an app restart (iOS
                // backgrounding the PWA mid-session evicts the WASM runtime), so once that
                // record is gone a later stale pull silently reverts the edit.
                if (pushedToCanonicalStore)
                {
                    // pushedToCanonicalStore means the phone's own write was just
                    // accepted directly into finance_sync (the canonical snapshot
                    // AutoSyncAsync pulls from) — when HasCloudSync that's PushFullSyncAsync
                    // succeeding, not merely phone_push/PC accepting a mailbox copy. The
                    // phone is the writer in that path, so there is nothing further to
                    // "confirm" by re-fetching the cloud: trust it immediately. When cloud
                    // sync isn't configured, pushedToCanonicalStore falls back to the PC
                    // accepting the push directly — still a genuine write, just on the
                    // background/local-only path the PC case is meant to be.
                    //
                    // Reference-based removal (every list below, not just trips): several
                    // queueing methods (QueueUpdatedTransaction, MarkBillPaidAsync,
                    // QueueUpdatedTripAsync, the Debt/Account/SavingsGoal/Setting update
                    // helpers) replace a pending entry for the same id by removing it and
                    // re-adding it at the end of the list. If the user edits the same record
                    // again while this push is still in flight, that replace happens on the
                    // live list *after* the snapshot above was taken — count-based
                    // RemoveRange(0, push.X.Count) would then delete the newer, never-sent
                    // edit instead of the one actually pushed, and the edit silently reverts
                    // on the next pull. Removing each pushed object by reference instead only
                    // ever removes the exact object that was sent, so a newer replacement
                    // (a different object instance) always survives. Likewise, a persisted
                    // override/tombstone row for that id is only cleared once nothing for
                    // that id remains pending — otherwise the row is the only on-disk record
                    // defending the newer edit against a stale pull.
                    foreach (var t in push.UpdatedTransactions)
                    {
                        _pendingUpdatedTransactions.Remove(t);
                        if (!_pendingUpdatedTransactions.Any(p => p.Id == t.Id))
                            await db.ClearTransactionOverrideAsync(t.Id);
                    }
                    foreach (var t in push.NewTransactions)
                        _pendingNewTransactions.Remove(t);
                    foreach (var id in push.DeletedTransactionIds)
                        _pendingDeletedTransactionIds.Remove(id);
                    foreach (var d in push.DeletedTransactions)
                    {
                        _pendingDeletedTransactions.Remove(d);
                        await db.ClearTransactionDeleteAsync(PendingTransactionDelete.GetStableId(d));
                    }

                    foreach (var s in push.UpdatedBillStatuses)
                    {
                        _pendingBillStatuses.Remove(s);
                        if (!_pendingBillStatuses.Any(p => p.BillId == s.BillId))
                            await db.ClearBillOverrideAsync(s.BillId);
                    }
                    foreach (var b in push.NewBills)
                        _pendingNewBills.Remove(b);
                    foreach (var b in push.UpdatedBills)
                    {
                        _pendingUpdatedBills.Remove(b);
                        if (!_pendingUpdatedBills.Any(p => p.Id == b.Id))
                            await db.ClearBillEditOverrideAsync(b.Id);
                    }
                    foreach (var id in push.DeletedBillIds)
                        _pendingDeletedBillIds.Remove(id);
                    foreach (var d in push.DeletedBills)
                        _pendingDeletedBills.Remove(d);

                    foreach (var id in push.DeletedDebtIds.Where(id => id > 0))
                        await db.ClearDebtDeleteAsync(id);
                    foreach (var d in push.NewDebts)
                        _pendingNewDebts.Remove(d);
                    foreach (var d in push.UpdatedDebts)
                    {
                        _pendingUpdatedDebts.Remove(d);
                        if (!_pendingUpdatedDebts.Any(p => p.Id == d.Id))
                            await db.ClearDebtOverrideAsync(d.Id);
                    }
                    foreach (var id in push.DeletedDebtIds)
                        _pendingDeletedDebtIds.Remove(id);
                    foreach (var p in push.NewDebtPayments)
                        _pendingNewDebtPayments.Remove(p);
                    foreach (var id in push.DeletedDebtPaymentIds)
                        _pendingDeletedDebtPaymentIds.Remove(id);

                    foreach (var a in push.UpdatedAccounts)
                    {
                        _pendingUpdatedAccounts.Remove(a);
                        if (!_pendingUpdatedAccounts.Any(p => p.Id == a.Id))
                            await db.ClearAccountOverrideAsync(a.Id);
                    }

                    foreach (var s in push.UpdatedSettings)
                    {
                        _pendingUpdatedSettings.Remove(s);
                        if (!_pendingUpdatedSettings.Any(p => p.Key == s.Key))
                            await db.ClearSettingOverrideAsync(s.Key);
                    }

                    // Savings goal tombstones are deliberately NOT cleared here. A
                    // successful push only means Supabase/the PC's inbox accepted the
                    // delete, not that WPF has reconciled it into the canonical
                    // finance_sync snapshot yet — that can lag well past this sync
                    // round. Clearing the tombstone this early left nothing to defend
                    // against a pull that still has the goal, so it would silently
                    // reappear once ConfirmedPushGrace ran out (and a subsequent edit
                    // on the revived copy would then vanish the next time the server's
                    // delayed delete finally landed). ApplyPersistedSavingsGoalDeletesAsync
                    // clears it instead, once a freshly-pulled snapshot actually confirms
                    // the goal is gone.
                    foreach (var g in push.NewSavingsGoals)
                        _pendingNewSavingsGoals.Remove(g);
                    foreach (var g in push.UpdatedSavingsGoals)
                    {
                        _pendingUpdatedSavingsGoals.Remove(g);
                        if (!_pendingUpdatedSavingsGoals.Any(p => p.Id == g.Id))
                            await db.ClearSavingsGoalOverrideAsync(g.Id);
                    }
                    foreach (var id in push.DeletedSavingsGoalIds)
                        _pendingDeletedSavingsGoalIds.Remove(id);

                    foreach (var id in push.DeletedTripIds.Where(id => id > 0))
                        await db.ClearTripDeleteAsync(id);
                    foreach (var t in push.NewTrips)
                    {
                        _pendingNewTrips.Remove(t);
                        var stillPending = _pendingNewTrips.Any(p => p.Id == t.Id);
                        LogTrip($"CLEAR-CHECK-NEW trip={t.Id} sentWas=[{ItinSnapshot(t)}] stillPending={stillPending} clearingOverride={!stillPending}");
                        if (!stillPending)
                            await db.ClearTripOverrideAsync(t.Id);
                    }
                    foreach (var t in push.UpdatedTrips)
                    {
                        _pendingUpdatedTrips.Remove(t);
                        var stillPending = _pendingUpdatedTrips.Any(p => p.Id == t.Id);
                        LogTrip($"CLEAR-CHECK-UPDATED trip={t.Id} sentWas=[{ItinSnapshot(t)}] stillPending={stillPending} clearingOverride={!stillPending}");
                        if (!stillPending)
                            await db.ClearTripOverrideAsync(t.Id);
                    }
                    foreach (var id in push.DeletedTripIds)
                        _pendingDeletedTripIds.Remove(id);
                }
            }

            // Pull data — cloud first, then local Wi-Fi
            var ok = await sync.AutoSyncAsync();
            if (ok)
            {
                // Hold the Trip mutation gate for the whole replace-then-correct
                // window: LoadAsync() wholesale-replaces Trip objects from
                // IndexedDB (which may still be stale pending the reapply below),
                // so a concurrent AddTripAsync/UpdateTripAsync/DeleteTripAsync must
                // wait rather than clone/queue a half-corrected Trip.
                await _tripMutationGate.WaitAsync();
                try
                {
                    var tripsBeforeLoad = Trips.Where(t => t.Id < 0).Select(CloneTrip).ToList();
                    _suppressLoadOnChange = true;
                    _suppressPendingReseed = true;
                    try { await LoadAsync(); }
                    finally { _suppressLoadOnChange = false; _suppressPendingReseed = false; }
                    AdoptPulledTripIds(tripsBeforeLoad);
                    LastSyncChangeSummary = BuildSyncChangeSummary(beforeTransactions, beforeBills, beforeDebts, beforeDebtPayments);
                    // Sync wipes IndexedDB and replaces with server data; reapply any
                    // phone-side changes that weren't pushed so they aren't lost.
                    if (sentPush is not null)
                    {
                        // An unrelated edit (e.g. a bill toggle) can trigger a push of its
                        // own while an earlier push is still within its grace window —
                        // that new push's lists are empty for every entity type it didn't
                        // touch. Replacing _lastConfirmedPush wholesale with it would drop
                        // the defense for the earlier entity (e.g. a just-ticked trip
                        // checklist item) before the server has actually reconciled it,
                        // letting the next stale pull silently revert it. Merge instead of
                        // replace so still-in-grace entities keep being defended.
                        var merging = _lastConfirmedPush is not null && DateTime.UtcNow - _lastConfirmedPushAt < ConfirmedPushGrace;
                        var defended = merging ? MergeConfirmedPush(sentPush, _lastConfirmedPush!) : sentPush;
                        LogTrip($"DEFEND merging={merging} defendedTrips=[{string.Join(" || ", defended.UpdatedTrips.Select(t => ItinSnapshot(t)))}]");
                        await ReapplyPushChangesAsync(defended);
                        _lastConfirmedPush = defended;
                        _lastConfirmedPushAt = DateTime.UtcNow;
                    }
                    else if (_lastConfirmedPush is not null && DateTime.UtcNow - _lastConfirmedPushAt < ConfirmedPushGrace)
                    {
                        // Nothing new to push this round, but a recently-confirmed push may
                        // not have propagated through the PC's reconciliation cycle yet —
                        // keep defending it until the grace period elapses.
                        LogTrip($"DEFEND-STALE lastConfirmedTrips=[{string.Join(" || ", _lastConfirmedPush.UpdatedTrips.Select(t => ItinSnapshot(t)))}]");
                        await ReapplyPushChangesAsync(_lastConfirmedPush);
                    }
                    LogTrip($"REAPPLY-PENDING pendingTrips=[{string.Join(" || ", _pendingUpdatedTrips.Select(t => ItinSnapshot(t)))}]");
                    await ReapplyPendingChangesAsync();
                }
                finally { _tripMutationGate.Release(); }
            }
            else OnChange?.Invoke();
        }
        finally
        {
            _syncInProgress = false;
            OnChange?.Invoke();
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
        await db.ClearBillEditOverridesAsync();
        await db.ClearBillDeletesAsync();
        await db.ClearDebtDeletesAsync();
        await db.ClearDebtOverridesAsync();
        await db.ClearSavingsGoalDeletesAsync();
        await db.ClearSavingsGoalOverridesAsync();
        await db.ClearTripDeletesAsync();
        await db.ClearAccountOverridesAsync();
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
        _lastConfirmedPush = null;
        await MarkPendingChangesSyncedAsync();
        await db.ClearTransactionOverridesAsync();
        await db.ClearTransactionDeletesAsync();
        await db.ClearBillOverridesAsync();
        await db.ClearBillEditOverridesAsync();
        await db.ClearBillDeletesAsync();
        await db.ClearDebtDeletesAsync();
        await db.ClearDebtOverridesAsync();
        await db.ClearSavingsGoalDeletesAsync();
        await db.ClearSavingsGoalOverridesAsync();
        await db.ClearTripDeletesAsync();
        await db.ClearTripOverridesAsync();
        await db.ClearAccountOverridesAsync();
        await db.ClearSettingOverridesAsync();
        LastSyncChangeSummary = "Pending phone-side sync intents were cleared. Existing finance data was left alone.";
        await LoadAsync();
    }

    private static PushPayload MergeConfirmedPush(PushPayload newer, PushPayload older) => new()
    {
        NewTransactions = MergeById(newer.NewTransactions, older.NewTransactions, t => t.Id),
        UpdatedTransactions = MergeById(newer.UpdatedTransactions, older.UpdatedTransactions, t => t.Id),
        DeletedTransactionIds = newer.DeletedTransactionIds.Union(older.DeletedTransactionIds).ToList(),
        TransactionEdits = MergeById(newer.TransactionEdits, older.TransactionEdits, t => t.Id),
        DeletedTransactions = MergeById(newer.DeletedTransactions, older.DeletedTransactions, PendingTransactionDelete.GetStableId),
        UpdatedBillStatuses = MergeById(newer.UpdatedBillStatuses, older.UpdatedBillStatuses, s => s.BillId),
        UpdatedSettings = MergeById(newer.UpdatedSettings, older.UpdatedSettings, s => (object)s.Key),
        NewBills = MergeById(newer.NewBills, older.NewBills, b => b.Id),
        UpdatedBills = MergeById(newer.UpdatedBills, older.UpdatedBills, b => b.Id),
        DeletedBillIds = newer.DeletedBillIds.Union(older.DeletedBillIds).ToList(),
        DeletedBills = MergeById(newer.DeletedBills, older.DeletedBills, b => b.Id),
        NewDebts = MergeById(newer.NewDebts, older.NewDebts, d => d.Id),
        UpdatedDebts = MergeById(newer.UpdatedDebts, older.UpdatedDebts, d => d.Id),
        DeletedDebtIds = newer.DeletedDebtIds.Union(older.DeletedDebtIds).ToList(),
        NewDebtPayments = MergeById(newer.NewDebtPayments, older.NewDebtPayments, p => p.Id),
        DeletedDebtPaymentIds = newer.DeletedDebtPaymentIds.Union(older.DeletedDebtPaymentIds).ToList(),
        UpdatedAccounts = MergeById(newer.UpdatedAccounts, older.UpdatedAccounts, a => a.Id),
        NewSavingsGoals = MergeById(newer.NewSavingsGoals, older.NewSavingsGoals, g => g.Id),
        UpdatedSavingsGoals = MergeById(newer.UpdatedSavingsGoals, older.UpdatedSavingsGoals, g => g.Id),
        DeletedSavingsGoalIds = newer.DeletedSavingsGoalIds.Union(older.DeletedSavingsGoalIds).ToList(),
        NewTrips = MergeById(newer.NewTrips, older.NewTrips, t => t.Id),
        UpdatedTrips = MergeById(newer.UpdatedTrips, older.UpdatedTrips, t => t.Id),
        DeletedTripIds = newer.DeletedTripIds.Union(older.DeletedTripIds).ToList()
    };

    // Keeps every entry from `newer`, plus any entry from `older` whose key
    // doesn't appear in `newer` — so a fresh push that's empty for some
    // entity type doesn't drop an earlier, still-unconfirmed entry for that
    // same type.
    private static List<T> MergeById<T>(List<T> newer, List<T> older, Func<T, object> keySelector)
    {
        if (older.Count == 0) return newer;
        if (newer.Count == 0) return older;
        var newerKeys = newer.Select(keySelector).ToHashSet();
        return newer.Concat(older.Where(o => !newerKeys.Contains(keySelector(o)))).ToList();
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
            t.IsReimbursement = pt.IsReimbursement;
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
                IsUnnecessary = edit.IsUnnecessary,
                IsReimbursement = edit.IsReimbursement
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

        // Re-add phone-created savings goals the server doesn't know about yet.
        // Skip any that match a delete tombstone — this push payload was
        // captured before a delete that happened while this sync round was
        // already in flight, and a not-yet-synced goal that got deleted has
        // no other defense against being blindly re-added here.
        var pendingGoalDeletesForReapply = await db.GetPendingSavingsGoalDeletesAsync();
        foreach (var g in push.NewSavingsGoals)
        {
            if (pendingGoalDeletesForReapply.Any(d => SameSavingsGoalDelete(g, d)))
            {
                LogGoal($"REAPPLY-SKIP-DELETED {GoalSnapshot(g)}");
                continue;
            }
            var existingNewGoal = SavingsGoals.FirstOrDefault(x => x.Id == g.Id);
            if (existingNewGoal is null)
            {
                // IndexedDbService.PreserveLocalSavingsGoalsWhenMissingAsync already
                // merged this negative-Id goal onto the server's real-Id record
                // (by content match) before the wholesale reload above — re-adding
                // the stale negative-Id object here would create a duplicate.
                var alreadyAdopted = SavingsGoals.FirstOrDefault(x => x.Id > 0
                    && string.Equals(x.Name.Trim(), g.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals((x.GroupName ?? "").Trim(), (g.GroupName ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (alreadyAdopted is not null)
                {
                    LogGoal($"REAPPLY-ADOPTED oldId={g.Id} {GoalSnapshot(alreadyAdopted)}");
                    continue;
                }

                LogGoal($"REAPPLY-READD {GoalSnapshot(g)}");
                SavingsGoals.Add(g);
                await db.PutAsync("savingsGoals", g);
                changed = true;
            }
            else if (existingNewGoal.Name != g.Name || existingNewGoal.TargetCents != g.TargetCents
                || existingNewGoal.CurrentCents != g.CurrentCents || existingNewGoal.WeeklyContributionCents != g.WeeklyContributionCents
                || existingNewGoal.TargetDate != g.TargetDate || existingNewGoal.GroupName != g.GroupName)
            {
                // A not-yet-synced goal's edits are never queued into
                // _pendingUpdatedSavingsGoals (there's nothing server-side to
                // "update" yet) — they only ever live on this same object
                // reference inside _pendingNewSavingsGoals. The wholesale
                // reload above just replaced SavingsGoals with fresh objects
                // straight from IndexedDB, so any edit made while this sync
                // round was running needs to be copied back over here or it's
                // silently lost the moment the user looks away.
                LogGoal($"REAPPLY-RESYNC existing=[{GoalSnapshot(existingNewGoal)}] pending=[{GoalSnapshot(g)}]");
                existingNewGoal.Name = g.Name;
                existingNewGoal.TargetCents = g.TargetCents;
                existingNewGoal.CurrentCents = g.CurrentCents;
                existingNewGoal.WeeklyContributionCents = g.WeeklyContributionCents;
                existingNewGoal.TargetDate = g.TargetDate;
                existingNewGoal.GroupName = g.GroupName;
                await db.PutAsync("savingsGoals", existingNewGoal);
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
                LogGoal($"REAPPLY-REREMOVE id={id}");
                await db.DeleteAsync("savingsGoals", id);
                changed = true;
            }
        }

        // Re-add phone-created trips the server doesn't know about yet. If the
        // server actually accepted this trip already and assigned it a real Id
        // (the common case — push+pull both happen in this same sync round trip),
        // adopt that Id onto the freshly-pulled record instead of re-adding the
        // stale negative-Id object as a separate entry: a true duplicate would
        // silently lose every edit made on the orphaned negative-Id copy the
        // moment the *next* sync wipes Trips again, since nothing tracks it once
        // it's no longer in any pending queue.
        foreach (var t in push.NewTrips)
        {
            // Still stuck at the same negative Id (never adopted) — overwrite the
            // existing entry with this freshly defended/pushed clone's fields
            // instead of skipping, or a pending edit captured in this push gets
            // silently dropped every time the trip is already present locally
            // (which it always is, once created).
            var existingSameId = Trips.FirstOrDefault(x => x.Id == t.Id);
            if (existingSameId is not null)
            {
                LogTrip($"REAPPLY-NEW trip={t.Id} current {ItinSnapshot(existingSameId)} incoming {ItinSnapshot(t)}");
                CopyTripFields(t, existingSameId);
                await db.PutAsync("trips", existingSameId);
                changed = true;
                continue;
            }

            var adopted = Trips.FirstOrDefault(x => SameTripAdoptionCandidate(x, t));
            if (adopted is not null)
            {
                CopyTripFields(t, adopted);
                await db.PutAsync("trips", adopted);
                AdoptTripId(t.Id, adopted.Id);
                changed = true;
                continue;
            }

            Trips.Add(t);
            await db.PutAsync("trips", t);
            changed = true;
        }

        // Re-apply trip edits made on phone
        foreach (var ut in push.UpdatedTrips)
        {
            var trip = Trips.FirstOrDefault(x => x.Id == ut.Id);
            if (trip is null) continue;
            LogTrip($"REAPPLY trip={ut.Id} current {ItinSnapshot(trip)} incoming {ItinSnapshot(ut)}");
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
