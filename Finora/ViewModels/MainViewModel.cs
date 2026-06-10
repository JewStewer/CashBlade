using Finora.Data;
using Finora.Models;
using Finora.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Finora.ViewModels;

public class MainViewModel : ViewModelBase
{
    private const string NextPayDateSettingKey = "NextPayDate";
    private const string SummaryPeriodSettingKey = "SummaryPeriod";
    private const string BudgetIncludedItemsSettingKey = "BudgetIncludedItems";
    private const string BudgetExcludedItemsSettingKey = "BudgetExcludedItems";
    private const string BudgetTransferTargetsSettingKey = "BudgetTransferTargets";
    private const string CustomBudgetItemsSettingKey = "CustomBudgetItems";
    private const string DebtPaymentPeriodSettingKey = "DebtPaymentPeriod";
    private const string BudgetSnapshotsSettingKey = "BudgetSnapshots";
    private const string TransactionRulesSettingKey = "TransactionRules";
    private const string CategoryLimitsSettingKey = "CategoryLimits";
    private const string TemplateBudgetPrefix = "Template ";
    private const string AffordabilityAmountSettingKey = "AffordabilityAmount";
    private const string AffordabilityWeeksSettingKey = "AffordabilityWeeks";
    private const string AffordabilitySafetyBufferSettingKey = "AffordabilitySafetyBuffer";
    private const string AffordabilityAccountNameSettingKey = "AffordabilityAccountName";
    private const string EmergencyFundAccountNameSettingKey = "EmergencyFundAccountName";
    private const string ShowAllTransactionsSettingKey = "ShowAllTransactions";
    private const string IgnoredSubscriptionsSettingKey = "IgnoredSubscriptions";
    private const string SavingsBudgetRecommendationDeclinedSettingKey = "SavingsBudgetRecommendationDeclined";
    private const string SuggestedSavingsBudgetName = "Budget planner savings";
    private readonly List<AccountRow> _allAccountRows = new();
    private readonly HashSet<string> _ignoredSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<TransactionRow> Transactions { get; } = new();
    public ObservableCollection<BillDueSoonRow> BillsDueNext7Days { get; } = new();
    public ObservableCollection<MonthlyTrendRow> MonthlyTrend { get; } = new();
    public ObservableCollection<AccountRow> Accounts { get; } = new();
    public ObservableCollection<BillRow> Bills { get; } = new();
    public ObservableCollection<BillMatchReviewRow> BillMatchReviews { get; } = new();
    public ObservableCollection<BillPaymentHistoryRow> BillPaymentHistory { get; } = new();
    public ObservableCollection<CoverTransferGroupRow> CoverTransferGroups { get; } = new();
    public ObservableCollection<BillCalendarDayRow> BillCalendarDays { get; } = new();
    public ObservableCollection<CategoryRow> Categories { get; } = new();
    public ObservableCollection<DebtRow> Debts { get; } = new();
    public ObservableCollection<SavingsGoalRow> SavingsGoals { get; } = new();
    public ObservableCollection<ReportChartRow> SpendingBillRatioRows { get; } = new();
    public ObservableCollection<ReportChartRow> CategorySpendingRows { get; } = new();
    public ObservableCollection<ReportChartRow> MerchantSpendingRows { get; } = new();
    public ObservableCollection<ReportChartRow> IncomeCategoryRows { get; } = new();
    public ObservableCollection<ReportChartRow> MonthlyCashFlowRows { get; } = new();
    public ObservableCollection<BudgetVarianceRow> BudgetVarianceRows { get; } = new();
    public ObservableCollection<SavedBudgetTileRow> SavedBudgetTiles { get; } = new();
    /// <summary>One tile per individual bill showing its weekly equivalent cost.</summary>
    public ObservableCollection<SavedBudgetTileRow> BudgetBillDetailTiles { get; } = new();
    /// <summary>Per-bill coverage rows for the "Can I afford this?" tool.</summary>
    public ObservableCollection<AffordabilityBillCoverageRow> AffordabilityBillRows { get; } = new();
    /// <summary>Week-by-week projected balance rows.</summary>
    public ObservableCollection<AffordabilityWeekRow> AffordabilityWeekRows { get; } = new();
    public ObservableCollection<CashForecastRow> CashForecastRows { get; } = new();
    public ObservableCollection<CashForecastChartRow> CashForecastChartRows { get; } = new();
    public ObservableCollection<AccountProjectionRow> AccountProjections { get; } = new();
    public ObservableCollection<RecurringPaymentRow> RecurringPayments { get; } = new();
    public ObservableCollection<AccountFundingBillRow> SelectedAccountFundingBills { get; } = new();
    public ObservableCollection<BudgetBreakdownRow> BudgetBreakdownRows { get; } = new();
    public ObservableCollection<BudgetBreakdownGroup> BudgetBreakdownGroups { get; } = new();
    public ObservableCollection<BillFundingPlanRow> BillFundingPlanRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> BudgetInsightRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> CashFlowInsights { get; } = new();
    public ObservableCollection<BudgetInsightRow> BillSaverInsights { get; } = new();
    public ObservableCollection<FinancialHealthComponent> FinancialHealthComponents { get; } = new();
    public ObservableCollection<BudgetInsightRow> SubscriptionInsights { get; } = new();
    public ObservableCollection<BudgetInsightRow> SpendingInsights { get; } = new();
    public ObservableCollection<BudgetInsightRow> BudgetHealthInsights { get; } = new();
    public ObservableCollection<BudgetInsightRow> GoalInsights { get; } = new();
    public ObservableCollection<BudgetInsightRow> CleanupInsights { get; } = new();
    public ObservableCollection<DebtPayoffPlanRow> DebtPayoffPlanRows { get; } = new();
    public ObservableCollection<DebtPaymentAuditRow> DebtPaymentAuditRows { get; } = new();
    public ObservableCollection<DebtStrategyRow> DebtStrategyRows { get; } = new();
    public ObservableCollection<DangerAlertRow> DangerAlerts { get; } = new();
    public ObservableCollection<BudgetSnapshotRow> BudgetSnapshots { get; } = new();
    public ObservableCollection<TransactionRuleRow> TransactionRules { get; } = new();
    public ObservableCollection<CategoryLimitRow> CategoryLimits { get; } = new();
    public ObservableCollection<TransactionSearchRow> TransactionSearchResults { get; } = new();
    public ObservableCollection<BudgetInsightRow> PaydayChecklistRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> SpendingLeakRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> SubscriptionCleanupRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> GoalMomentumRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> AccountHealthRows { get; } = new();
    public ObservableCollection<BudgetInsightRow> DebtAcceleratorRows { get; } = new();
    public ObservableCollection<ReportChartRow> InsightsCategoryRows { get; } = new();
    public ObservableCollection<DailyTrackerDayRow> DailyTrackerDays { get; } = new();
    public ObservableCollection<string> BudgetTransferAccountOptions { get; } = new();
    public IReadOnlyList<string> BudgetCategoryOptions { get; } = new[] { "Bills", "Essentials", "Savings", "Unplanned" };
    public IReadOnlyList<string> DebtPaymentPeriodOptions { get; } = new[] { "Weekly", "Fortnightly", "Monthly" };
    public IReadOnlyList<string> ForecastRangeOptions { get; } = new[] { "6 weeks", "8 weeks", "3 months" };
    public IReadOnlyList<string> AccountSortOptions { get; } = new[] { "Needs funding", "Name", "Balance high", "Balance low", "Type", "Target progress" };
    public IReadOnlyList<string> TransactionRuleCategoryOptions => Categories.Select(c => c.Name).ToList();

    private decimal _totalBalance;
    public decimal TotalBalance { get => _totalBalance; set => SetProperty(ref _totalBalance, value); }

    private decimal _billsBalance;
    public decimal BillsBalance { get => _billsBalance; set => SetProperty(ref _billsBalance, value); }

    private decimal _savingsTotal;
    public decimal SavingsTotal { get => _savingsTotal; set => SetProperty(ref _savingsTotal, value); }

    private decimal _debtTotal;
    public decimal DebtTotal { get => _debtTotal; set => SetProperty(ref _debtTotal, value); }

    private decimal _netWorth;
    public decimal NetWorth { get => _netWorth; set => SetProperty(ref _netWorth, value); }

    private int _financialHealthScore;
    public int FinancialHealthScore { get => _financialHealthScore; set => SetProperty(ref _financialHealthScore, value); }

    private string _financialHealthGrade = "—";
    public string FinancialHealthGrade { get => _financialHealthGrade; set => SetProperty(ref _financialHealthGrade, value); }

    private string _financialHealthColor = "#94A3B8";
    public string FinancialHealthColor { get => _financialHealthColor; set => SetProperty(ref _financialHealthColor, value); }

    private int _budgetStreakWeeks;
    public int BudgetStreakWeeks { get => _budgetStreakWeeks; set => SetProperty(ref _budgetStreakWeeks, value); }

    private string _budgetStreakDisplay = "—";
    public string BudgetStreakDisplay { get => _budgetStreakDisplay; set => SetProperty(ref _budgetStreakDisplay, value); }

    private int _unnecessaryStreakDays;
    public int UnnecessaryStreakDays { get => _unnecessaryStreakDays; set => SetProperty(ref _unnecessaryStreakDays, value); }

    private string _unnecessaryStreakDisplay = "Mark your first transaction to start your streak.";
    public string UnnecessaryStreakDisplay { get => _unnecessaryStreakDisplay; set => SetProperty(ref _unnecessaryStreakDisplay, value); }

    private decimal _todayUnnecessarySpending;
    public decimal TodayUnnecessarySpending { get => _todayUnnecessarySpending; set => SetProperty(ref _todayUnnecessarySpending, value); }

    private decimal _todayNecessarySpending;
    public decimal TodayNecessarySpending { get => _todayNecessarySpending; set => SetProperty(ref _todayNecessarySpending, value); }

    public ObservableCollection<DailyCategoryRow> DailyTopCategories { get; } = new();
    public ObservableCollection<WeekCalendarDay> WeekCalendarDays { get; } = new();
    public ObservableCollection<MonthCalendarCell> MonthCalendarCells { get; } = new();

    private string _weekLabel = string.Empty;
    public string WeekLabel { get => _weekLabel; set => SetProperty(ref _weekLabel, value); }

    private decimal _periodTotalSpending;
    public decimal PeriodTotalSpending { get => _periodTotalSpending; set => SetProperty(ref _periodTotalSpending, value); }

    private decimal _periodUnnecessarySpending;
    public decimal PeriodUnnecessarySpending { get => _periodUnnecessarySpending; set => SetProperty(ref _periodUnnecessarySpending, value); }

    private int _weeklyScore = 100;
    public int WeeklyScore
    {
        get => _weeklyScore;
        set
        {
            if (SetProperty(ref _weeklyScore, value))
            {
                OnPropertyChanged(nameof(WeeklyGrade));
                OnPropertyChanged(nameof(WeeklyGradeColorHex));
            }
        }
    }
    public string WeeklyGrade => WeeklyScore switch
    {
        100 => "A+",
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "F"
    };
    public string WeeklyGradeColorHex => WeeklyScore switch
    {
        100 => "#34D399",
        >= 80 => "#6EE7B7",
        >= 60 => "#FBBF24",
        >= 40 => "#F97316",
        _ => "#F87171"
    };

    private decimal _lastWeekTotalSpending;
    public decimal LastWeekTotalSpending { get => _lastWeekTotalSpending; set => SetProperty(ref _lastWeekTotalSpending, value); }

    private decimal _weekOnWeekChange;
    public decimal WeekOnWeekChange
    {
        get => _weekOnWeekChange;
        set
        {
            if (SetProperty(ref _weekOnWeekChange, value))
            {
                OnPropertyChanged(nameof(WeekOnWeekChangeDisplay));
                OnPropertyChanged(nameof(WeekOnWeekColorHex));
            }
        }
    }
    public string WeekOnWeekChangeDisplay => LastWeekTotalSpending == 0 ? "no data last week"
        : WeekOnWeekChange > 0 ? $"+{WeekOnWeekChange:C} vs last week"
        : WeekOnWeekChange < 0 ? $"{Math.Abs(WeekOnWeekChange):C} less than last week"
        : "same as last week";
    public string WeekOnWeekColorHex => WeekOnWeekChange > 0 ? "#F87171" : WeekOnWeekChange < 0 ? "#34D399" : "#64748B";

    private decimal _safeToSpendToday;
    public decimal SafeToSpendToday
    {
        get => _safeToSpendToday;
        set
        {
            if (SetProperty(ref _safeToSpendToday, value))
                OnPropertyChanged(nameof(SafeToSpendTodayDisplay));
        }
    }
    public string SafeToSpendTodayDisplay => SafeToSpendToday > 0 ? $"{SafeToSpendToday:C}" : "over budget";
    public bool HasBudgetSetUp => SafeToSpendAmount > 0;

    private decimal _debtStrategyExtraPayment;
    public decimal DebtStrategyExtraPayment
    {
        get => _debtStrategyExtraPayment;
        set
        {
            if (SetProperty(ref _debtStrategyExtraPayment, Math.Max(value, 0)))
            {
                LoadDebtStrategies();
            }
        }
    }

    private string _debtStrategyExtraPaymentPeriod = "Monthly";
    public string DebtStrategyExtraPaymentPeriod
    {
        get => _debtStrategyExtraPaymentPeriod;
        set
        {
            value = NormalizeDebtPaymentPeriod(value);
            if (SetProperty(ref _debtStrategyExtraPaymentPeriod, value))
            {
                LoadDebtStrategies();
            }
        }
    }

    private bool _debtStrategyRollsOverMinimums;
    public bool DebtStrategyRollsOverMinimums
    {
        get => _debtStrategyRollsOverMinimums;
        set
        {
            if (SetProperty(ref _debtStrategyRollsOverMinimums, value))
            {
                LoadDebtStrategies();
            }
        }
    }

    private DateTime? _debtFreeTargetDate = DateTime.Today.AddYears(2);
    public DateTime? DebtFreeTargetDate
    {
        get => _debtFreeTargetDate;
        set
        {
            if (SetProperty(ref _debtFreeTargetDate, value?.Date))
            {
                RefreshDebtFreeTarget();
            }
        }
    }

    private string _forecastRange = "6 weeks";
    public string ForecastRange
    {
        get => _forecastRange;
        set
        {
            value = ForecastRangeOptions.Contains(value) ? value : "6 weeks";
            if (SetProperty(ref _forecastRange, value))
            {
                LoadCashForecast();
            }
        }
    }

    private string _accountSearchText = string.Empty;
    public string AccountSearchText
    {
        get => _accountSearchText;
        set
        {
            if (SetProperty(ref _accountSearchText, value))
            {
                ApplyAccountFilters();
            }
        }
    }

    private string _accountSortOption = "Needs funding";
    public string AccountSortOption
    {
        get => _accountSortOption;
        set
        {
            value = AccountSortOptions.Contains(value) ? value : "Needs funding";
            if (SetProperty(ref _accountSortOption, value))
            {
                ApplyAccountFilters();
            }
        }
    }

    private string _newRuleContainsText = string.Empty;
    public string NewRuleContainsText { get => _newRuleContainsText; set => SetProperty(ref _newRuleContainsText, value); }

    private string _newRuleCategoryName = string.Empty;
    public string NewRuleCategoryName { get => _newRuleCategoryName; set => SetProperty(ref _newRuleCategoryName, value); }

    private string _newRuleDisplayName = string.Empty;
    public string NewRuleDisplayName { get => _newRuleDisplayName; set => SetProperty(ref _newRuleDisplayName, value); }

    private string _newLimitCategory = string.Empty;
    public string NewLimitCategory { get => _newLimitCategory; set => SetProperty(ref _newLimitCategory, value); }

    private string _newLimitAmount = string.Empty;
    public string NewLimitAmount { get => _newLimitAmount; set => SetProperty(ref _newLimitAmount, value); }

    private string _transactionSearchQuery = string.Empty;
    public string TransactionSearchQuery { get => _transactionSearchQuery; set => SetProperty(ref _transactionSearchQuery, value); }

    public decimal DebtOriginalTotal => Debts.Sum(d => d.OriginalBalance);
    public decimal DebtPaidTotal => RoundDollars(DebtOriginalTotal - DebtTotal);
    public decimal DebtProgressPercent => DebtOriginalTotal <= 0 ? 0 : Math.Round(DebtPaidTotal / DebtOriginalTotal, 4);
    public string DebtProgressSummary => DebtOriginalTotal <= 0
        ? "Add debts to track payoff progress."
        : $"{DebtPaidTotal:C} paid off from {DebtOriginalTotal:C} total.";
    public string HighestInterestDebtSummary
    {
        get
        {
            var debt = Debts
                .Where(d => d.InterestRate is not null && d.Balance > 0)
                .OrderByDescending(d => d.InterestRate)
                .FirstOrDefault();
            return debt is null
                ? "No interest rates set."
                : $"{debt.Name} at {debt.InterestRate:0.##}%";
        }
    }
    public string NextDebtPayoffSummary
    {
        get
        {
            var row = DebtPayoffPlanRows
                .Where(r => r.EstimatedPaidOff is not null)
                .OrderBy(r => r.EstimatedPaidOff)
                .FirstOrDefault();
            return row is null
                ? "Set payments to estimate payoff dates."
                : $"{row.Name} by {row.EstimatedPaidOffDisplay}";
        }
    }
    public int DebtStrategyActiveCount => Debts.Count(d => d.Balance > 0 && d.IncludeInStrategy);
    public decimal DebtStrategyMonthlyExtraPayment => ConvertPaymentToMonthly(DebtStrategyExtraPayment, DebtStrategyExtraPaymentPeriod);
    public decimal DebtStrategySelectedBalance => Debts
        .Where(d => d.Balance > 0 && d.IncludeInStrategy)
        .Sum(d => d.Balance);
    public decimal DebtStrategySelectedMonthlyMinimums => Debts
        .Where(d => d.Balance > 0 && d.IncludeInStrategy)
        .Sum(d => ConvertPaymentToMonthly(d.MinimumPayment, d.PaymentPeriod));
    public decimal DebtStrategyExcludedBalance => Debts
        .Where(d => d.Balance > 0 && !d.IncludeInStrategy)
        .Sum(d => d.Balance);
    public string DebtStrategyExcludedSummary => DebtStrategyExcludedBalance <= 0
        ? "No active debts excluded."
        : $"{DebtStrategyExcludedBalance:C} excluded from this model.";
    public string DebtStrategyHighestSelectedRateSummary
    {
        get
        {
            var debt = Debts
                .Where(d => d.Balance > 0 && d.IncludeInStrategy)
                .OrderByDescending(d => d.InterestRate ?? 0)
                .FirstOrDefault();
            return debt is null
                ? "No selected debts."
                : $"{debt.Name} at {debt.InterestRate.GetValueOrDefault():0.##}%";
        }
    }
    public string DebtStrategyActiveSummary => DebtStrategyActiveCount == 0
        ? "No active debts selected."
        : $"{DebtStrategyActiveCount} active debt{(DebtStrategyActiveCount == 1 ? "" : "s")} selected.";
    public string DebtStrategyExtraSummary => DebtStrategyMonthlyExtraPayment <= 0
        ? "No extra payment modelled."
        : $"{DebtStrategyExtraPayment:C}/{DebtStrategyExtraPaymentPeriod.ToLowerInvariant()} extra modelled.";
    public string DebtStrategySelectedBalanceSummary => DebtStrategySelectedBalance <= 0
        ? "No selected balance."
        : $"{DebtStrategySelectedBalance:C} selected.";
    public string DebtStrategyPaymentPoolSummary => DebtStrategyActiveCount == 0
        ? "Select debts to model payments."
        : $"{(DebtStrategySelectedMonthlyMinimums + DebtStrategyMonthlyExtraPayment):C}/month total modelled.";
    public string DebtStrategyRolloverSummary => DebtStrategyRollsOverMinimums
        ? "Paid-off minimums roll into the next target."
        : "Paid-off minimums are not rolled over.";
    public CashForecastRow? ForecastLowPoint => CashForecastRows.OrderBy(r => r.ProjectedBalance).FirstOrDefault();
    public CashForecastRow? ForecastEndPoint => CashForecastRows.LastOrDefault();
    public string ForecastLowPointSummary => ForecastLowPoint is null
        ? "No forecast events yet."
        : $"{ForecastLowPoint.ProjectedBalance:C} on {ForecastLowPoint.Date:dd/MM/yyyy}";
    public string ForecastEndBalanceSummary => ForecastEndPoint is null
        ? "No forecast events yet."
        : $"{ForecastEndPoint.ProjectedBalance:C} by {ForecastEndPoint.Date:dd/MM/yyyy}";
    public decimal ForecastBillsTotal => RoundDollars(CashForecastRows.Where(r => r.Change < 0).Sum(r => Math.Abs(r.Change)));
    public decimal ForecastIncomeTotal => RoundDollars(CashForecastRows.Where(r => r.Change > 0).Sum(r => r.Change));
    public string ForecastEventSummary => CashForecastRows.Count == 0
        ? "No upcoming events in range."
        : $"{CashForecastRows.Count} event{(CashForecastRows.Count == 1 ? "" : "s")} in {ForecastRange}.";
    public string CommandCenterStatus
    {
        get
        {
            if (BillsFundingShortfall > 0)
            {
                return "Funding attention";
            }

            if (BudgetPlannerIncomeGap < 0)
            {
                return "Plan over income";
            }

            if (ForecastLowPoint is not null && ForecastLowPoint.ProjectedBalance < 0)
            {
                return "Forecast shortfall";
            }

            if (DebtTotal > 0 && DebtStrategyMonthlyExtraPayment <= 0)
            {
                return "Debt plan idle";
            }

            return "On track";
        }
    }
    public string CommandCenterStatusColorHex => CommandCenterStatus switch
    {
        "On track" => "#6EE7B7",
        "Debt plan idle" => "#FBBF24",
        _ => "#F87171"
    };
    public string? CommandCenterNavTarget => CommandCenterStatus switch
    {
        "Funding attention" => "Bills",
        "Plan over income" => "Budget",
        "Forecast shortfall" => "Planning",
        "Debt plan idle" => "Debts",
        _ => null
    };
    public bool HasCommandCenterAction => !string.IsNullOrEmpty(CommandCenterNavTarget);
    public string CommandCenterNavLabel => CommandCenterNavTarget switch
    {
        "Planning" => "Go to Insights",
        null or "" => string.Empty,
        _ => $"Go to {CommandCenterNavTarget}"
    };
    public string CommandCenterPrimaryMove
    {
        get
        {
            if (BillsFundingShortfall > 0)
            {
                return $"Move {BillsFundingShortfall:C} into bill savers before payday.";
            }

            if (BudgetPlannerIncomeGap < 0)
            {
                return $"Trim {-BudgetPlannerIncomeGap:C}/week from the plan or raise income.";
            }

            if (ForecastLowPoint is not null && ForecastLowPoint.ProjectedBalance < 0)
            {
                return $"Protect cash before {ForecastLowPoint.Date:dd/MM/yyyy}; forecast dips to {ForecastLowPoint.ProjectedBalance:C}.";
            }

            if (RecurringPayments.Any(r => !r.IsAlreadyBill))
            {
                var count = RecurringPayments.Count(r => !r.IsAlreadyBill);
                return $"Review {count} recurring payment{(count == 1 ? "" : "s")} not yet set up as bills.";
            }

            return $"{SafeToSpendAmount:C} is free after planned transfers and essentials.";
        }
    }
    public string CommandCenterSecondaryMove => ForecastLowPoint is null
        ? "Build a forecast by adding bills, income, and recurring payments."
        : $"Lowest forecast point: {ForecastLowPoint.ProjectedBalance:C} on {ForecastLowPoint.Date:dd/MM/yyyy}.";
    public string CashRunwaySummary
    {
        get
        {
            if (AverageDailySpending <= 0)
            {
                return "Add spending history to calculate runway.";
            }

            var days = (int)Math.Floor(SafeToSpendAmount / AverageDailySpending);
            return days <= 0
                ? "Safe money is already fully allocated."
                : $"{days} day{(days == 1 ? "" : "s")} at {AverageDailySpending:C}/day.";
        }
    }
    public string UpcomingSqueezeSummary
    {
        get
        {
            var low = ForecastLowPoint;
            return low is null
                ? "No dated cash events in the current range."
                : $"{low.Date:dd MMM}: {low.ProjectedBalance:C} after {low.Description}.";
        }
    }
    public string CashRunwayExplanation =>
        "Days your Safe to Spend balance would last at your recent average daily spending. More days means more breathing room before payday.";
    public string UpcomingSqueezeExplanation =>
        "The lowest point your projected balance reaches in the cash flow forecast, and the bill or expense that causes it.";
    public decimal SubscriptionWeeklyTotal => RoundDollars(RecurringPayments.Sum(r => r.WeeklyAmount));
    public int SubscriptionsNotInBillsCount => RecurringPayments.Count(r => !r.IsAlreadyBill);
    public string SubscriptionCommandSummary => RecurringPayments.Count == 0
        ? "No recurring subscriptions detected yet."
        : $"{RecurringPayments.Count} recurring payments, {SubscriptionWeeklyTotal:C}/week total.";
    public string AccountCoverageSummary => Accounts.Count == 0
        ? "No accounts loaded."
        : $"{Accounts.Count(a => a.NeededNow > 0)} account{(Accounts.Count(a => a.NeededNow > 0) == 1 ? "" : "s")} need funding now.";
    public string GoalCommandSummary => SavingsGoals.Count == 0
        ? "No goals yet."
        : $"{SavingsGoals.Count} goal{(SavingsGoals.Count == 1 ? "" : "s")} tracked, {SavingsGoals.Sum(g => g.WeeklyContribution):C}/week committed.";
    public string DebtFreeTargetExtraSummary
    {
        get
        {
            var activeDebts = Debts.Where(d => d.Balance > 0 && d.IncludeInStrategy).ToList();
            if (activeDebts.Count == 0)
            {
                return "Select debts to calculate a target payment.";
            }

            if (DebtFreeTargetDate is null || DebtFreeTargetDate.Value.Date <= DateTime.Today)
            {
                return "Pick a future date.";
            }

            var targetMonths = Math.Max(1, ((DebtFreeTargetDate.Value.Year - DateTime.Today.Year) * 12) + DebtFreeTargetDate.Value.Month - DateTime.Today.Month);
            var currentExtra = DebtStrategyMonthlyExtraPayment;
            var current = SimulateDebtStrategy(activeDebts.OrderByDescending(d => d.InterestRate ?? 0).ThenBy(d => d.Balance).ToList(), currentExtra, DebtStrategyRollsOverMinimums);
            if (current.Months > 0 && current.Months <= targetMonths)
            {
                return $"Current extra payment reaches the selected debts by {DebtFreeTargetDate.Value:MMM yyyy}.";
            }

            var required = EstimateRequiredMonthlyExtra(activeDebts, targetMonths, DebtStrategyRollsOverMinimums);
            if (required is null)
            {
                return "Target date is too aggressive for the current debt data.";
            }

            var additional = Math.Max(required.Value - currentExtra, 0);
            return $"Needs about {required.Value:C}/month extra, roughly {additional:C}/month more than now.";
        }
    }
    public string BestDebtStrategyTitle
    {
        get
        {
            var best = DebtStrategyRows
                .Where(r => r.MonthsRemaining > 0)
                .OrderBy(r => r.InterestPaid)
                .ThenBy(r => r.MonthsRemaining)
                .FirstOrDefault();
            return best is null ? "No strategy winner yet" : $"{best.Strategy} saves most interest";
        }
    }
    public string BestDebtStrategySummary
    {
        get
        {
            var rows = DebtStrategyRows
                .Where(r => r.MonthsRemaining > 0)
                .OrderBy(r => r.InterestPaid)
                .ThenBy(r => r.MonthsRemaining)
                .ToList();
            if (rows.Count == 0)
            {
                return "Add debts with payments to compare avalanche and snowball.";
            }

            if (rows.Count == 1)
            {
                return $"{rows[0].Strategy} pays off in {rows[0].MonthsRemaining} months with about {rows[0].InterestPaid:C} interest.";
            }

            var best = rows[0];
            var other = rows[1];
            var interestSaved = Math.Max(other.InterestPaid - best.InterestPaid, 0);
            var monthsSaved = Math.Max(other.MonthsRemaining - best.MonthsRemaining, 0);
            return $"{best.Strategy} saves about {interestSaved:C} interest and {monthsSaved} month{(monthsSaved == 1 ? "" : "s")} versus {other.Strategy}.";
        }
    }

    private decimal _billsOwedTotal;
    public decimal BillsOwedTotal { get => _billsOwedTotal; set => SetProperty(ref _billsOwedTotal, value); }

    private decimal _weeklyIncome;
    public decimal WeeklyIncome
    {
        get => _weeklyIncome;
        set
        {
            if (SetProperty(ref _weeklyIncome, value))
            {
                RefreshBudgetDerivedValues();
            }
        }
    }

    private decimal _budgetBills;
    public decimal BudgetBills
    {
        get => _budgetBills;
        set
        {
            if (SetProperty(ref _budgetBills, value))
            {
                RefreshBudgetDerivedValues();
            }
        }
    }

    private decimal _budgetEssentials;
    public decimal BudgetEssentials
    {
        get => _budgetEssentials;
        set
        {
            if (SetProperty(ref _budgetEssentials, value))
            {
                RefreshBudgetDerivedValues();
            }
        }
    }

    private decimal _budgetSavings;
    public decimal BudgetSavings
    {
        get => _budgetSavings;
        set
        {
            if (SetProperty(ref _budgetSavings, value))
            {
                RefreshBudgetDerivedValues();
            }
        }
    }

    private decimal _budgetUnplanned;
    public decimal BudgetUnplanned
    {
        get => _budgetUnplanned;
        set
        {
            if (SetProperty(ref _budgetUnplanned, value))
            {
                RefreshBudgetDerivedValues();
            }
        }
    }

    public decimal BudgetLeftover => WeeklyIncome - BudgetBills - BudgetEssentials - BudgetSavings - BudgetUnplanned;
    public decimal BudgetTotal => BudgetBills + BudgetEssentials + BudgetSavings + BudgetUnplanned;

    // % of weekly income for each budget category — used on the category tiles
    public string BudgetBillsShare      => WeeklyIncome <= 0 ? "" : $"{BudgetBills      / WeeklyIncome:P0} of income";
    public string BudgetEssentialsShare => WeeklyIncome <= 0 ? "" : $"{BudgetEssentials / WeeklyIncome:P0} of income";
    public string BudgetSavingsShare    => WeeklyIncome <= 0 ? "" : $"{BudgetSavings    / WeeklyIncome:P0} of income";
    public string BudgetUnplannedShare  => WeeklyIncome <= 0 ? "" : $"{BudgetUnplanned  / WeeklyIncome:P0} of income";
    public string BudgetLeftoverShare   => WeeklyIncome <= 0 ? "" : $"{Math.Max(BudgetLeftover / WeeklyIncome, 0):P0} remaining";
    public decimal BudgetUsagePercent => WeeklyIncome <= 0 ? 0 : Math.Round(BudgetTotal / WeeklyIncome, 4);
    public decimal BudgetRemainingPercent => WeeklyIncome <= 0 ? 0 : Math.Max(1 - BudgetUsagePercent, 0);
    public decimal BudgetSyncDifference => RoundDollars(BudgetPlannerWeeklyBills - BudgetBills);
    public bool HasBudgetSyncDifference => Math.Abs(BudgetSyncDifference) >= 0.01m;
    public string BudgetSyncMessage => Math.Abs(BudgetSyncDifference) < 0.01m
        ? "Budget bills match your current bill list."
        : BudgetSyncDifference > 0
            ? $"Current bills are {BudgetSyncDifference:C}/week higher than saved budget. Sync bills to budget."
            : $"Saved bill budget is {-BudgetSyncDifference:C}/week above current bills.";
    public string BudgetPressureTitle => WeeklyIncome <= 0
        ? "No income set"
        : BudgetUsagePercent >= 1m
            ? "Budget over income"
            : BudgetUsagePercent >= 0.9m
                ? "Budget is tight"
                : BudgetUsagePercent >= 0.75m
                    ? "Budget getting close"
                    : "Budget has room";
    public string BudgetPressureMessage => WeeklyIncome <= 0
        ? "Add income so Cashglade can judge whether the weekly budget is realistic."
        : BudgetUsagePercent >= 1m
            ? $"Your weekly budget is {-BudgetLeftover:C} over income."
            : BudgetUsagePercent >= 0.9m
                ? $"You are using {BudgetUsagePercent:P0} of weekly income. Only {BudgetLeftover:C} remains."
                : BudgetUsagePercent >= 0.75m
                    ? $"You are using {BudgetUsagePercent:P0} of weekly income. Keep an eye on extras."
                    : $"You are using {BudgetUsagePercent:P0} of weekly income with {BudgetLeftover:C} left.";
    private bool _isSavingsRecommendationIgnored;
    private bool _isSavingsRecommendationDeclined;
    private decimal _savingsRecommendationAmount;
    public bool ShowSavingsRecommendation => BudgetSavings == 0 &&
        !BudgetBreakdownRows.Any(r => r.IsIncluded && r.Bucket == "Savings" && r.Amount > 0) &&
        BudgetLeftover >= 5m &&
        WeeklyIncome > 0 &&
        !_isSavingsRecommendationIgnored &&
        !_isSavingsRecommendationDeclined;
    public decimal SuggestedSavingsRecommendationAmount => RoundDollars(Math.Min(BudgetLeftover, Math.Max(5m, BudgetLeftover * 0.5m)));
    public decimal SavingsRecommendationAmount
    {
        get => _savingsRecommendationAmount > 0 ? _savingsRecommendationAmount : SuggestedSavingsRecommendationAmount;
        set
        {
            if (SetProperty(ref _savingsRecommendationAmount, RoundDollars(value)))
            {
                OnPropertyChanged(nameof(SavingsRecommendation));
            }
        }
    }
    public string SavingsRecommendation => $"Suggested savings: {SavingsRecommendationAmount:C}/week. This does not require a savings account; it adds a savings allocation to the budget.";
    public decimal BudgetPlannerWeeklyBills
    {
        get
        {
            var breakdownBills = BudgetBreakdownRows
                .Where(r => r.IsIncluded && (r.Bucket == "Bills" || IsAccountTargetBudgetRow(r)))
                .Sum(r => r.Amount);

            return breakdownBills > 0
                ? RoundDollars(breakdownBills)
                : RoundDollars(BillFundingPlanRows.Sum(r => r.WeeklyAmount));
        }
    }
    public decimal BudgetPlannerWeeklySavers => BudgetBreakdownRows
        .Where(r => r.Bucket != "Bills" && !string.IsNullOrWhiteSpace(r.TransferTo))
        .Sum(r => r.Amount);
    public decimal BudgetPlannerWeeklyTransfers => BudgetPlannerWeeklyBills + BudgetPlannerWeeklySavers;
    public decimal BudgetPlannerIncomeGap => WeeklyIncome - BudgetPlannerWeeklyTransfers - BudgetEssentials - BudgetUnplanned;
    public int DaysUntilPayday => Math.Max((NextPayDate.Date - DateTime.Today).Days, 0);
    public string DaysUntilPaydayDisplay => DaysUntilPayday == 0
        ? "Today"
        : $"{DaysUntilPayday} day{(DaysUntilPayday == 1 ? "" : "s")}";
    public decimal BillsDueBeforePayday => BillFundingPlanRows.Sum(r => r.DueBeforePayday);
    public decimal BillsDueOnPayday => BillFundingPlanRows.Sum(r => r.DueOnPayday);
    public int BillsDueOnPaydayCount => BillFundingPlanRows.Sum(r => r.DueOnPaydayCount);
    public decimal PaydayTransferTotal => BillFundingPlanRows.Sum(r => r.PaydayTransfer);
    public decimal BillsFundingShortfall => BillFundingPlanRows.Sum(r => r.NeededBeforePayday);
    // Subtracts the pre-payday funding shortfall so "safe to spend" doesn't count money
    // that must be moved to bill accounts before the next pay arrives. Also subtracts any
    // savings transfers already made this week beyond what the budget already plans for —
    // ad-hoc money moved to savings is no longer available to spend, even if unbudgeted.
    public decimal SafeToSpendAmount => Math.Max(WeeklyIncome - BudgetPlannerWeeklyTransfers - BudgetEssentials - BillsFundingShortfall
        - Math.Max(SavingsTransfersThisWeek - BudgetPlannerWeeklySavers, 0), 0);
    public bool HasBillsFundingShortfall => BillsFundingShortfall > 0;

    // Pre-payday cashflow projection
    public decimal PrePaydayBalance => RoundSignedDollars(TotalBalance - BillsDueBeforePayday);
    public decimal PostPaydayBalance => RoundSignedDollars(PrePaydayBalance + WeeklyIncome);
    public bool PrePaydayNegative => PrePaydayBalance < 0;
    public string PrePaydayBalanceColor => PrePaydayBalance < 0 ? "#F87171" : "#34D399";

    // Cash position card — uses bills through payday (before + on payday) to match account cards
    public decimal BillsDueThroughPayday => BillFundingPlanRows.Sum(r => r.DueThroughPayday);
    public decimal BalanceAfterAllBills => RoundSignedDollars(TotalBalance - BillsDueThroughPayday);
    public decimal BalanceAfterAllBillsPlusIncome => RoundSignedDollars(BalanceAfterAllBills + WeeklyIncome);
    public bool BalanceAfterAllBillsNegative => BalanceAfterAllBills < 0;
    public string BalanceAfterAllBillsColorHex => BalanceAfterAllBills < 0 ? "#F87171" : "#34D399";
    public string PaydayTransferPlanExplanation =>
        "Transfer is the amount to move into each account on payday. It uses the normal weekly bill portion unless that account needs a bigger top-up to cover bills due before or on payday.";

    private string _budgetTemplateName = string.Empty;
    public string BudgetTemplateName { get => _budgetTemplateName; set => SetProperty(ref _budgetTemplateName, value); }

    private decimal _budgetTemplateIncome;
    public decimal BudgetTemplateIncome { get => _budgetTemplateIncome; set => SetProperty(ref _budgetTemplateIncome, value); }

    private decimal _budgetTemplateBills;
    public decimal BudgetTemplateBills { get => _budgetTemplateBills; set => SetProperty(ref _budgetTemplateBills, value); }

    private decimal _budgetTemplateEssentials;
    public decimal BudgetTemplateEssentials { get => _budgetTemplateEssentials; set => SetProperty(ref _budgetTemplateEssentials, value); }

    private decimal _budgetTemplateSavings;
    public decimal BudgetTemplateSavings { get => _budgetTemplateSavings; set => SetProperty(ref _budgetTemplateSavings, value); }

    private decimal _budgetTemplateUnplanned;
    public decimal BudgetTemplateUnplanned { get => _budgetTemplateUnplanned; set => SetProperty(ref _budgetTemplateUnplanned, value); }

    public bool HasBudgetTemplateSuggestion => !string.IsNullOrWhiteSpace(BudgetTemplateName);
    public bool IsNormalTemplateActive => BudgetTemplateName == "Normal";
    public bool IsLeanTemplateActive => BudgetTemplateName == "Lean";
    public bool IsDebtTemplateActive => BudgetTemplateName == "Debt payoff";
    public decimal BudgetTemplateLeftover => BudgetTemplateIncome - BudgetTemplateBills - BudgetTemplateEssentials - BudgetTemplateSavings - BudgetTemplateUnplanned;
    public string BudgetTemplateSummary => HasBudgetTemplateSuggestion
        ? $"{BudgetTemplateName}: {BudgetTemplateBills:C} bills, {BudgetTemplateEssentials:C} essentials, {BudgetTemplateSavings:C} savings, {BudgetTemplateUnplanned:C} flexible, {BudgetTemplateLeftover:C} left."
        : "Choose a template to preview suggested weekly amounts.";
    public IReadOnlyList<BudgetBreakdownRow> BudgetTemplateSavingsBreakdown =>
        BudgetBreakdownRows
            .Where(r => r.Bucket == "Savings" && !string.IsNullOrWhiteSpace(r.TransferTo) && r.Amount > 0)
            .OrderByDescending(r => r.Amount)
            .ToList();
    public string BudgetTemplateSavingsNote
    {
        get
        {
            if (!HasBudgetTemplateSuggestion) return string.Empty;
            var goalNeeds = SavingsGoals.Where(g => g.WeeklyContribution > 0).Sum(g => g.WeeklyContribution);
            if (goalNeeds <= 0) return "No savings goals with weekly contributions set.";
            var spare = BudgetTemplateSavings - goalNeeds;
            return spare >= 0
                ? $"Covers your {goalNeeds:C}/wk across savings goals ({spare:C} to spare)."
                : $"Your savings goals need {goalNeeds:C}/wk — this template is {-spare:C} short.";
        }
    }
    public string SavedBudgetAllocationSummary => BudgetTotal <= 0
        ? "No saved budget amounts are allocated."
        : $"{BudgetTotal:C} allocated: bills {BudgetBills:C}, essentials {BudgetEssentials:C}, targets {BudgetSavings:C}, unplanned {BudgetUnplanned:C}.";
    public string SavedBudgetSourceSummary => HasBudgetTemplateSuggestion
        ? "Template suggestion is preview-only until you apply it."
        : "These are the real saved amounts currently used by Planning.";
    public string SavedBudgetTilesSummary => SavedBudgetTiles.Count == 0
        ? "No saved budget tiles yet."
        : $"{SavedBudgetTiles.Count} saved allocation tile{(SavedBudgetTiles.Count == 1 ? "" : "s")}.";

    private decimal _affordabilityAmount = 2000m;
    public decimal AffordabilityAmount
    {
        get => _affordabilityAmount;
        set
        {
            if (SetProperty(ref _affordabilityAmount, value))
            {
                RefreshAffordability();
                SaveAffordabilitySettings();
            }
        }
    }

    private int _affordabilityWeeks = 4;
    public int AffordabilityWeeks
    {
        get => _affordabilityWeeks;
        set
        {
            if (SetProperty(ref _affordabilityWeeks, Math.Max(value, 1)))
            {
                RefreshAffordability();
                SaveAffordabilitySettings();
            }
        }
    }

    private decimal _affordabilitySafetyBuffer = 100m;
    public decimal AffordabilitySafetyBuffer
    {
        get => _affordabilitySafetyBuffer;
        set
        {
            if (SetProperty(ref _affordabilitySafetyBuffer, Math.Max(value, 0)))
            {
                RefreshAffordability();
                SaveAffordabilitySettings();
            }
        }
    }

    private string _affordabilityAccountName = string.Empty;
    public string AffordabilityAccountName
    {
        get => _affordabilityAccountName;
        set
        {
            if (SetProperty(ref _affordabilityAccountName, value ?? string.Empty))
            {
                RefreshAffordability();
                SaveAffordabilitySettings();
            }
        }
    }

    private string _emergencyFundAccountName = string.Empty;
    public string EmergencyFundAccountName
    {
        get => _emergencyFundAccountName;
        set
        {
            if (SetProperty(ref _emergencyFundAccountName, value ?? string.Empty))
            {
                SaveEmergencyFundAccountSetting();
                OnPropertyChanged(nameof(EmergencyFundAccount));
                LoadTransactions(refreshInsights: false, refreshRecurring: false, refreshDependentViews: false);
            }
        }
    }

    public decimal AffordabilityWeeklyRequired => RoundDollars(AffordabilityAmount / Math.Max(AffordabilityWeeks, 1));
    public bool HasAffordabilityAccount => AffordabilityAccount is not null;
    public AccountRow? AffordabilityAccount => string.IsNullOrWhiteSpace(AffordabilityAccountName)
        ? null
        : _allAccountRows.FirstOrDefault(a => string.Equals(a.Name, AffordabilityAccountName, StringComparison.OrdinalIgnoreCase));
    public AccountRow? EmergencyFundAccount => string.IsNullOrWhiteSpace(EmergencyFundAccountName)
        ? null
        : _allAccountRows.FirstOrDefault(a => string.Equals(a.Name, EmergencyFundAccountName, StringComparison.OrdinalIgnoreCase));
    public decimal AffordabilityAccountBillsDue => GetAffordabilityAccountBillsDue();
    public decimal AffordabilityAccountBudgetedWeeklyTransfer => GetAffordabilityAccountBudgetedWeeklyTransfer();
    public decimal AffordabilityAccountProjectedTopUps => RoundDollars(AffordabilityAccountBudgetedWeeklyTransfer * Math.Max(AffordabilityWeeks, 1));
    public decimal AffordabilityAccountProjectedBalance => AffordabilityAccount is null
        ? 0
        : RoundSignedDollars(AffordabilityAccount.Balance + AffordabilityAccountProjectedTopUps - AffordabilityAccountBillsDue - AffordabilityAmount);
    public decimal AffordabilityAccountShortfall => Math.Max(-AffordabilityAccountProjectedBalance, 0);
    public decimal AffordabilityAccountExtraWeeklyNeeded => AffordabilityAccountShortfall <= 0
        ? 0
        : RoundDollars(AffordabilityAccountShortfall / Math.Max(AffordabilityWeeks, 1));
    public string AffordabilityAccountResult
    {
        get
        {
            if (AffordabilityAccount is null)
            {
                return "Choose an account to test account-level affordability.";
            }

            var account = AffordabilityAccount;
            if (AffordabilityAccountProjectedBalance >= 0)
            {
                return $"Yes. {account.Name} can cover {AffordabilityAmount:C}, {AffordabilityAccountBillsDue:C} of forecast bills, and {AffordabilityAccountProjectedTopUps:C} from budgeted transfers, leaving {AffordabilityAccountProjectedBalance:C}.";
            }

            var totalWeekly = RoundDollars(AffordabilityAccountBudgetedWeeklyTransfer + AffordabilityAccountExtraWeeklyNeeded);
            return $"No. {account.Name} would be short {AffordabilityAccountShortfall:C}. " +
                   $"Budget {totalWeekly:C}/wk total ({AffordabilityAccountExtraWeeklyNeeded:C} more than current {AffordabilityAccountBudgetedWeeklyTransfer:C}) " +
                   $"for {AffordabilityWeeks} week{(AffordabilityWeeks == 1 ? "" : "s")}.";
        }
    }
    public string AffordabilityAccountColorHex => AffordabilityAccount is null
        ? "#94A3B8"
        : AffordabilityAccountProjectedBalance >= 0
            ? "#6EE7B7"
            : "#F87171";
    public decimal AffordabilityWeeklyAvailable => RoundSignedDollars(BudgetLeftover);
    public decimal AffordabilityAvailable => RoundSignedDollars(BudgetLeftover * Math.Max(AffordabilityWeeks, 1));
    public decimal AffordabilityDifference => RoundSignedDollars(AffordabilityAvailable - AffordabilityAmount);
    public decimal AffordabilityWeeklyDifference => RoundSignedDollars(AffordabilityWeeklyAvailable - AffordabilityWeeklyRequired);
    public decimal AffordabilityMinimumWeeklyBuffer => AffordabilityWeeklyAvailable <= 0
        ? 0
        : RoundDollars(Math.Max(AffordabilitySafetyBuffer, AffordabilityWeeklyAvailable * 0.2m));
    public bool IsAffordabilityTight => AffordabilityAmount > 0 &&
        AffordabilityWeeklyDifference > 0 &&
        AffordabilityWeeklyDifference < AffordabilityMinimumWeeklyBuffer;
    public string AffordabilityStatus => AffordabilityAmount <= 0
        ? "Enter amount"
        : AffordabilityAccount is not null
            ? (AffordabilityAccountNowBalance < 0 ? "Can't afford"
              : AffordabilityAccountNowAfterBills < 0 ? "Short for bills"
              : AffordabilityAccountNowAfterBills < AffordabilitySafetyBuffer ? "Tight"
              : "Comfortable")
            : (AffordabilityWeeklyDifference <= 0 ? "Over budget"
              : IsAffordabilityTight ? "Tight"
              : "Comfortable");
    public decimal AffordabilitySpareAfterPurchase => RoundSignedDollars(AffordabilityWeeklyDifference);
    public string AffordabilitySpareTitle => AffordabilityAccount is not null ? "After withdrawal" : "Spare /week";
    public string AffordabilitySpareFormatted => AffordabilityAccount is not null
        ? $"{AffordabilityAccountNowBalance:C}"
        : $"{AffordabilityWeeklyDifference:C}";

    // Adaptive stat tiles — account-specific when an account is chosen, budget-based otherwise
    public string AffordabilityStat1Label => HasAffordabilityAccount ? "Account balance" : "Needs /week";
    public string AffordabilityStat1Value => HasAffordabilityAccount
        ? $"{AffordabilityAccount!.Balance:C}"
        : $"{AffordabilityWeeklyRequired:C}";
    public string AffordabilityStat2Label => HasAffordabilityAccount ? "After withdrawal" : "Budget spare /week";
    public string AffordabilityStat2Value => HasAffordabilityAccount
        ? $"{AffordabilityAccountNowBalance:C}"
        : $"{AffordabilityWeeklyAvailable:C}";
    public string AffordabilityStat2ColorHex => HasAffordabilityAccount
        ? (AffordabilityAccountNowBalance >= 0 ? "#6EE7B7" : "#F87171")
        : "#8B949E";
    public string AffordabilityStat3Label => HasAffordabilityAccount ? "After bills (now)" : "To account /wk";
    public string AffordabilityStat3Value => HasAffordabilityAccount
        ? (AffordabilityAccountBillsDue <= 0 ? "No bills due" : $"{AffordabilityAccountNowAfterBills:C}")
        : $"{AffordabilityAccountBudgetedWeeklyTransfer:C}";
    public string AffordabilityStat3ColorHex => HasAffordabilityAccount
        ? (AffordabilityAccountBillsDue <= 0 ? "#6E7681"
          : AffordabilityAccountNowAfterBills >= 0 ? "#6EE7B7" : "#F87171")
        : "#3FB950";
    public decimal AffordabilityAccountNowBalance => AffordabilityAccount is null
        ? 0
        : RoundSignedDollars(AffordabilityAccount.Balance - AffordabilityAmount);
    public decimal AffordabilityAccountNowAfterBills => AffordabilityAccount is null
        ? 0
        : RoundSignedDollars(AffordabilityAccount.Balance - AffordabilityAmount - AffordabilityAccountBillsDue);
    public string AffordabilityNowColorHex
    {
        get
        {
            if (AffordabilityAccount is null) return "#94A3B8";
            if (AffordabilityAccountNowBalance < 0) return "#F87171";
            if (AffordabilityAccountNowAfterBills < 0) return "#F59E0B";
            return "#6EE7B7";
        }
    }
    public string AffordabilityNowWithdrawalColorHex => AffordabilityAccount is null ? "#94A3B8" : AffordabilityAccountNowBalance >= 0 ? "#6EE7B7" : "#F87171";
    public string AffordabilityNowBillsColorHex => AffordabilityAccount is null ? "#94A3B8" : AffordabilityAccountNowAfterBills >= 0 ? "#6EE7B7" : "#F87171";
    public string AffordabilityNowWithdrawalLabel => $"{AffordabilityAccountNowBalance:C}";
    public string AffordabilityNowBillsLabel => AffordabilityAccountBillsDue <= 0 ? "No bills" : $"{AffordabilityAccountNowAfterBills:C}";
    public string AffordabilityNowLine
    {
        get
        {
            if (AffordabilityAccount is null) return string.Empty;
            var afterWithdrawal = AffordabilityAccountNowBalance;
            var afterBills = AffordabilityAccountNowAfterBills;
            var billsDue = AffordabilityAccountBillsDue;
            var weeks = Math.Max(AffordabilityWeeks, 1);
            var wkS = weeks == 1 ? "" : "s";
            if (afterWithdrawal < 0)
            {
                var shortfall = RoundDollars(-afterWithdrawal + Math.Max(billsDue, 0));
                var extra = RoundDollars(shortfall / weeks);
                var total = RoundDollars(AffordabilityAccountBudgetedWeeklyTransfer + extra);
                return $"Can't cover this yet — need {total:C}/wk for {weeks} week{wkS} ({extra:C} more than current).";
            }
            if (billsDue <= 0) return "Covered — no bills pending.";
            if (afterBills >= 0) return $"Covers withdrawal + {billsDue:C} bills. {afterBills:C} left.";
            var shortfall2 = RoundDollars(-afterBills);
            var extra2 = RoundDollars(shortfall2 / weeks);
            var total2 = RoundDollars(AffordabilityAccountBudgetedWeeklyTransfer + extra2);
            return $"Covers withdrawal but short {shortfall2:C} for bills — need {total2:C}/wk for {weeks} week{wkS} ({extra2:C} more).";
        }
    }
    public int AffordabilityRecommendedWeeks
    {
        get
        {
            if (AffordabilityAmount <= 0)
            {
                return 0;
            }

            var weeklyAvailableAfterBuffer = AffordabilityWeeklyAvailable - AffordabilityMinimumWeeklyBuffer;
            if (weeklyAvailableAfterBuffer <= 0)
            {
                return 0;
            }

            return Math.Max((int)Math.Ceiling(AffordabilityAmount / weeklyAvailableAfterBuffer), 1);
        }
    }
    public string AffordabilityBufferMessage => AffordabilityAmount <= 0
        ? "Enter an amount to see the buffer."
        : AffordabilityRecommendedWeeks <= 0
            ? $"Your current budget does not leave the {AffordabilityMinimumWeeklyBuffer:C}/week safety buffer."
            : AffordabilityRecommendedWeeks > AffordabilityWeeks
                ? $"Safer pace: {AffordabilityRecommendedWeeks} weeks keeps about {AffordabilityMinimumWeeklyBuffer:C}/week untouched."
                : $"This keeps at least {AffordabilityMinimumWeeklyBuffer:C}/week untouched.";
    public string AffordabilityColorHex => AffordabilityAmount <= 0
        ? "#94A3B8"
        : AffordabilityAccount is not null
            ? (AffordabilityAccountNowBalance < 0 ? "#F87171"
              : AffordabilityAccountNowAfterBills < 0 ? "#F59E0B"
              : AffordabilityAccountNowAfterBills < AffordabilitySafetyBuffer ? "#F59E0B"
              : "#6EE7B7")
            : (AffordabilityWeeklyDifference <= 0 ? "#F87171"
              : IsAffordabilityTight ? "#F59E0B"
              : "#6EE7B7");
    public string AffordabilityResult => AffordabilityAmount <= 0
        ? "Enter an amount to check."
        : AffordabilityAccount is not null
            ? (AffordabilityAccountNowBalance < 0
                ? $"No. {AffordabilityAccount.Name} has {AffordabilityAccount.Balance:C} which isn't enough to cover {AffordabilityAmount:C}."
              : AffordabilityAccountNowAfterBills < 0
                ? $"Partial. {AffordabilityAccount.Name} can cover the {AffordabilityAmount:C} ({AffordabilityAccountNowBalance:C} left), but is short {-AffordabilityAccountNowAfterBills:C} for {AffordabilityAccountBillsDue:C} in upcoming bills."
              : AffordabilityAccountBillsDue <= 0
                ? $"Yes. {AffordabilityAccount.Name} has {AffordabilityAccount.Balance:C}. After {AffordabilityAmount:C}, {AffordabilityAccountNowBalance:C} remains with no bills pending."
              : $"Yes. {AffordabilityAccount.Name} has {AffordabilityAccount.Balance:C}. After {AffordabilityAmount:C} and {AffordabilityAccountBillsDue:C} in bills, {AffordabilityAccountNowAfterBills:C} remains.")
            : (AffordabilityWeeklyDifference <= 0
                ? $"No. This needs {AffordabilityWeeklyRequired:C}/week but your budget leaves {AffordabilityWeeklyAvailable:C}/week. You are short {-AffordabilityWeeklyDifference:C}/week."
              : IsAffordabilityTight
                ? $"Tight. This fits, but only leaves {AffordabilityWeeklyDifference:C}/week spare."
              : $"Yes. This needs {AffordabilityWeeklyRequired:C}/week and your budget leaves {AffordabilityWeeklyAvailable:C}/week, with {AffordabilityWeeklyDifference:C}/week spare.");

    // ── New affordability properties ──────────────────────────────────────────
    /// <summary>Weekly instalment for BNPL spread. Equals the full amount when weeks = 1.</summary>
    public decimal AffordabilityWeeklyCost => RoundDollars(AffordabilityAmount / Math.Max(AffordabilityWeeks, 1));
    public string AffordabilityWeeklyCostDisplay => AffordabilityAmount <= 0
        ? "—"
        : AffordabilityWeeks <= 1
            ? $"{AffordabilityAmount:C} (lump sum)"
            : $"{AffordabilityWeeklyCost:C}/wk  ×  {AffordabilityWeeks} wks";
    public string AffordabilityWeeklyCostColorHex => AffordabilityAccount is null || AffordabilityAmount <= 0
        ? "#6E7681"
        : AffordabilityAccountBudgetedWeeklyTransfer >= AffordabilityWeeklyCost ? "#6EE7B7" : "#F59E0B";

    // Cashflow averages ────────────────────────────────────────────────────────
    private decimal _affordabilityWindowBillsTotal;
    public decimal AffordabilityWeeklyBills => _affordabilityWindowBillsTotal <= 0
        ? 0
        : RoundDollars(_affordabilityWindowBillsTotal / Math.Max(AffordabilityWeeks, 1));
    public decimal AffordabilityNetWeekly => RoundSignedDollars(
        AffordabilityAccountBudgetedWeeklyTransfer - AffordabilityWeeklyCost - AffordabilityWeeklyBills);
    public bool HasAffordabilityInputs => HasAffordabilityAccount && AffordabilityAmount > 0;

    // Week projection backing fields ───────────────────────────────────────────
    private decimal _affordabilityLowestBalance;
    private int _affordabilityLowestBalanceWeek;
    private decimal _affordabilityEndBalanceWith;
    private decimal _affordabilityEndBalanceWithout;

    public bool HasAffordabilityWeekRows => AffordabilityWeekRows.Count > 0;
    public decimal AffordabilityLowestBalance => _affordabilityLowestBalance;
    public string AffordabilityLowestBalanceDisplay => $"{_affordabilityLowestBalance:C}";
    public string AffordabilityEndBalanceWithDisplay => $"{_affordabilityEndBalanceWith:C}";
    public string AffordabilityEndBalanceWithoutDisplay => $"{_affordabilityEndBalanceWithout:C}";
    public string AffordabilityEndBalanceWithColorHex => _affordabilityEndBalanceWith >= 0 ? "#6EE7B7" : "#F87171";
    public string AffordabilityEndBalanceWithoutColorHex => _affordabilityEndBalanceWithout >= 0 ? "#6EE7B7" : "#F87171";
    public string AffordabilityWeeksSuffix => $"after {Math.Max(AffordabilityWeeks, 1)} week{(AffordabilityWeeks == 1 ? "" : "s")}";

    // Status system ────────────────────────────────────────────────────────────
    // 0 = Comfortable, 1 = Tight, 2 = Risky, 3 = Unsafe, -1 = no input
    private int GetAffordabilityStatusLevel()
    {
        if (AffordabilityAmount <= 0 || AffordabilityAccount is null) return -1;
        if (_affordabilityLowestBalance < 0) return 3;
        if (AffordabilityNetWeekly < -5m) return 2;
        if (_affordabilityLowestBalance < 50m || AffordabilityCashflowUsagePct > 80) return 1;
        return 0;
    }
    public string AffordabilityStatusIcon => GetAffordabilityStatusLevel() switch
    {
        3 or 2 => "✕",
        1 => "⚠",
        0 => "✓",
        _ => ""
    };
    public string AffordabilityStatusLabel => GetAffordabilityStatusLevel() switch
    {
        3 => "Unsafe",
        2 => "Risky",
        1 => "Tight",
        0 => "Comfortable",
        _ => ""
    };
    public string AffordabilityStatusColorHex => GetAffordabilityStatusLevel() switch
    {
        3 or 2 => "#F87171",
        1 => "#F59E0B",
        0 => "#6EE7B7",
        _ => "#6E7681"
    };
    public string AffordabilityStatusDetail
    {
        get
        {
            if (AffordabilityAmount <= 0 || AffordabilityAccount is null) return string.Empty;
            var level = GetAffordabilityStatusLevel();
            var shortCount = AffordabilityShortBillCount;
            var wk = _affordabilityLowestBalanceWeek;
            return level switch
            {
                3 => $"Account projected to go negative in Week {wk} (lowest: {_affordabilityLowestBalance:C})." +
                     (shortCount > 0 ? $" {shortCount} bill{(shortCount == 1 ? "" : "s")} at risk of missing payments." : ""),
                2 => $"Net cash is {AffordabilityNetWeekly:C}/wk — balance declines over time." +
                     (shortCount > 0 ? $" {shortCount} bill{(shortCount == 1 ? "" : "s")} may be missed." : ""),
                1 => $"Bills covered but balance dips low — min {_affordabilityLowestBalance:C} in Week {wk}. Purchase uses {AffordabilityCashflowUsagePct:F0}% of weekly income.",
                _ => $"All bills covered. Lowest balance: {_affordabilityLowestBalance:C} (Week {wk}). Purchase uses {AffordabilityCashflowUsagePct:F0}% of weekly income."
            };
        }
    }

    /// <summary>Purchase cost as % of the budgeted weekly top-up.</summary>
    public decimal AffordabilityCashflowUsagePct
    {
        get
        {
            var topUp = AffordabilityAccountBudgetedWeeklyTransfer;
            if (topUp <= 0 || AffordabilityWeeklyCost <= 0) return 0;
            return Math.Round(AffordabilityWeeklyCost / topUp * 100, 0);
        }
    }

    // Bill coverage ────────────────────────────────────────────────────────────
    public bool HasAffordabilityBillRows => AffordabilityBillRows.Count > 0;
    public bool AffordabilityAllBillsCovered => AffordabilityBillRows.Count == 0 || AffordabilityBillRows.All(r => r.IsCovered);
    public int AffordabilityShortBillCount => AffordabilityBillRows.Count(r => !r.IsCovered);
    public decimal AffordabilityTotalExtraWeeklyNeeded => AffordabilityBillRows.Where(r => !r.IsCovered).Select(r => r.ExtraWeeklyNeeded).DefaultIfEmpty(0m).Max();
    public decimal AffordabilityBalanceAfterPurchase => AffordabilityAccount is null
        ? 0 : RoundSignedDollars(AffordabilityAccount.Balance - AffordabilityAmount);

    // Action / verdict ─────────────────────────────────────────────────────────
    public bool HasAffordabilityAction => HasAffordabilityInputs && !AffordabilityAllBillsCovered;
    public string AffordabilityActionText => HasAffordabilityAction
        ? $"To cover all bills: add {AffordabilityTotalExtraWeeklyNeeded:C}/wk to {AffordabilityAccount!.Name}."
        : string.Empty;

    // Keep for backward compat (some older bindings)
    public string AffordabilityVerdictText => AffordabilityActionText;
    public string AffordabilityVerdictColorHex => HasAffordabilityAction ? "#F59E0B" : "#6EE7B7";

    private string _customBudgetName = string.Empty;
    public string CustomBudgetName { get => _customBudgetName; set => SetProperty(ref _customBudgetName, value); }

    private decimal _customBudgetAmount;
    public decimal CustomBudgetAmount { get => _customBudgetAmount; set => SetProperty(ref _customBudgetAmount, value); }

    private string _customBudgetBucket = "Unplanned";
    public string CustomBudgetBucket { get => _customBudgetBucket; set => SetProperty(ref _customBudgetBucket, value); }

    private string _customBudgetTransferTo = string.Empty;
    public string CustomBudgetTransferTo { get => _customBudgetTransferTo; set => SetProperty(ref _customBudgetTransferTo, value); }

    private string _summaryPeriod = "Monthly";
    public string SummaryPeriod
    {
        get => _summaryPeriod;
        set
        {
            if (SetProperty(ref _summaryPeriod, value))
            {
                if (value == "Weekly")
                {
                    WeeklyPageOffset = 0;
                }
                else if (value == "Monthly")
                {
                    MonthlyPageOffset = 0;
                }

                LoadTransactions();
                OnPropertyChanged(nameof(BillCalendarTitle));
                OnPropertyChanged(nameof(BillCalendarSubtitle));
                OnPropertyChanged(nameof(IsMonthlyCalendar));
                OnPropertyChanged(nameof(BillCalendarModeSummary));
                SaveSummaryPeriod(value);
            }
        }
    }

    private string _debtPaymentPeriod = "Weekly";
    public string DebtPaymentPeriod
    {
        get => _debtPaymentPeriod;
        set
        {
            if (value is not ("Weekly" or "Fortnightly" or "Monthly"))
            {
                value = "Weekly";
            }

            if (SetProperty(ref _debtPaymentPeriod, value))
            {
                LoadDebtPayoffPlan();
                OnPropertyChanged(nameof(NextDebtPayoffSummary));
                SaveDebtPaymentPeriod(value);
            }
        }
    }

    private int _weeklyPageOffset;
    public int WeeklyPageOffset
    {
        get => _weeklyPageOffset;
        private set
        {
            if (SetProperty(ref _weeklyPageOffset, value))
            {
                OnPropertyChanged(nameof(SummaryDateRange));
            }
        }
    }

    public string SummaryDateRange
    {
        get
        {
            if (SummaryPeriod == "All")
            {
                return "All transactions";
            }

            var (start, end) = GetSummaryPeriodRange();
            return $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
        }
    }

    private int _monthlyPageOffset;
    public int MonthlyPageOffset
    {
        get => _monthlyPageOffset;
        private set
        {
            if (SetProperty(ref _monthlyPageOffset, value))
            {
                OnPropertyChanged(nameof(SummaryDateRange));
            }
        }
    }

    private string _transactionSearch = string.Empty;
    public string TransactionSearch
    {
        get => _transactionSearch;
        set
        {
            if (SetProperty(ref _transactionSearch, value))
            {
                LoadTransactions();
            }
        }
    }

    private bool _showUncategorisedOnly;
    public bool ShowUncategorisedOnly
    {
        get => _showUncategorisedOnly;
        set
        {
            if (SetProperty(ref _showUncategorisedOnly, value))
            {
                OnPropertyChanged(nameof(TransactionFilterLabel));
                LoadTransactions();
            }
        }
    }

    private bool _hideCoverTransfers = true;
    public bool HideCoverTransfers
    {
        get => _hideCoverTransfers;
        set
        {
            if (SetProperty(ref _hideCoverTransfers, value))
            {
                OnPropertyChanged(nameof(HideCoverTransfersButtonText));
                OnPropertyChanged(nameof(TransactionFilterLabel));
                LoadTransactions();
            }
        }
    }

    public string HideCoverTransfersButtonText => HideCoverTransfers ? "Show cover rows" : "Hide cover rows";

    private bool _showAllTransactions = false;
    public bool ShowAllTransactions
    {
        get => _showAllTransactions;
        set
        {
            if (SetProperty(ref _showAllTransactions, value))
            {
                LoadTransactions();
            }
        }
    }

    private int _transactionTotalCount;
    public int TransactionTotalCount { get => _transactionTotalCount; set => SetProperty(ref _transactionTotalCount, value); }

    private int _transactionShownCount;
    public int TransactionShownCount { get => _transactionShownCount; set => SetProperty(ref _transactionShownCount, value); }

    public bool HasHiddenTransactions => !ShowAllTransactions && TransactionTotalCount > TransactionShownCount;
    public string TransactionCountSummary => HasHiddenTransactions
        ? $"Showing {TransactionShownCount} of {TransactionTotalCount} transactions (last 6 months)"
        : $"Showing all {TransactionShownCount} transactions";

    public int BillsDueSoonCount => Bills.Count(b => !b.IsPaid && b.DueDate.Date <= NextPayDate.Date);
    public bool HasBillsDueNext7Days => BillsDueNext7Days.Count > 0;

    public int BillsNeedingFundingCount => Bills.Count(b => !b.IsPaid && b.NeededNow > 0);

    public int BillsPaidThisPeriodCount => Bills.Count(b => b.IsPaid);

    public int AmbiguousBillMatchCount => BillMatchReviews.Count;

    private decimal _periodSpending;
    public decimal PeriodSpending { get => _periodSpending; set => SetProperty(ref _periodSpending, value); }

    private decimal _periodIncome;
    public decimal PeriodIncome { get => _periodIncome; set => SetProperty(ref _periodIncome, value); }

    private decimal _periodBillsDue;
    public decimal PeriodBillsDue { get => _periodBillsDue; set => SetProperty(ref _periodBillsDue, value); }

    private decimal _periodNetCashFlow;
    public decimal PeriodNetCashFlow { get => _periodNetCashFlow; set => SetProperty(ref _periodNetCashFlow, value); }

    private decimal _averageDailySpending;
    public decimal AverageDailySpending { get => _averageDailySpending; set => SetProperty(ref _averageDailySpending, value); }

    private decimal _savingsRate;
    public decimal SavingsRate { get => _savingsRate; set => SetProperty(ref _savingsRate, value); }

    private decimal _savingsAmountThisPeriod;
    public decimal SavingsAmountThisPeriod { get => _savingsAmountThisPeriod; set => SetProperty(ref _savingsAmountThisPeriod, value); }

    private decimal _savingsTransfersThisWeek;
    /// <summary>
    /// Money already moved into Savings-type accounts during the current pay week,
    /// regardless of the SummaryPeriod filter. Used by SafeToSpendAmount so ad-hoc
    /// savings transfers reduce what's left to spend even when not budgeted.
    /// </summary>
    public decimal SavingsTransfersThisWeek
    {
        get => _savingsTransfersThisWeek;
        set
        {
            if (SetProperty(ref _savingsTransfersThisWeek, value))
            {
                OnPropertyChanged(nameof(SafeToSpendAmount));
            }
        }
    }

    private string _largestSpendingCategory = "None";
    public string LargestSpendingCategory { get => _largestSpendingCategory; set => SetProperty(ref _largestSpendingCategory, value); }

    private decimal _largestSpendingCategoryAmount;
    public decimal LargestSpendingCategoryAmount { get => _largestSpendingCategoryAmount; set => SetProperty(ref _largestSpendingCategoryAmount, value); }

    private string _transactionTypeFilter = "All";
    public string TransactionTypeFilter
    {
        get => _transactionTypeFilter;
        private set
        {
            if (SetProperty(ref _transactionTypeFilter, value))
            {
                OnPropertyChanged(nameof(TransactionFilterLabel));
            }
        }
    }

    public string TransactionFilterLabel => ShowUncategorisedOnly
        ? "Needs category"
        : TransactionTypeFilter == "All"
            ? HideCoverTransfers ? "All types, cover rows hidden" : "All types"
            : HideCoverTransfers && TransactionTypeFilter == "Transfers" ? "Transfers, cover rows hidden" : TransactionTypeFilter;

    private DateTime _nextPayDate = DateTime.Today;
    public DateTime NextPayDate
    {
        get => _nextPayDate;
        private set
        {
            if (SetProperty(ref _nextPayDate, value))
            {
                OnPropertyChanged(nameof(DaysUntilPayday));
                OnPropertyChanged(nameof(DaysUntilPaydayDisplay));
            }
        }
    }

    private TransactionRow? _selectedTransaction;
    public TransactionRow? SelectedTransaction { get => _selectedTransaction; set => SetProperty(ref _selectedTransaction, value); }

    private AccountRow? _selectedAccount;
    public AccountRow? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                LoadSelectedAccountFundingBills();
                OnPropertyChanged(nameof(SelectedAccountFundingTitle));
                OnPropertyChanged(nameof(SelectedAccountAuditSummary));
            }
        }
    }

    public string SelectedAccountFundingTitle => SelectedAccount is null
        ? "Select an account"
        : $"{SelectedAccount.Name} bills before payday";

    private string _selectedAccountFundingMessage = "Select an account to see its bills before payday.";
    public string SelectedAccountFundingMessage
    {
        get => _selectedAccountFundingMessage;
        set => SetProperty(ref _selectedAccountFundingMessage, value);
    }

    public string SelectedAccountAuditSummary
    {
        get
        {
            if (SelectedAccount is null)
            {
                return "Choose an account to audit its upcoming bills.";
            }

            var total = SelectedAccountFundingBills.Sum(b => b.Amount);
            var needed = SelectedAccountFundingBills.Sum(b => b.NeededNow);
            return needed <= 0
                ? $"{SelectedAccount.Name}: {SelectedAccountFundingBills.Count} upcoming bill{(SelectedAccountFundingBills.Count == 1 ? "" : "s")} covered, {total:C} due through payday."
                : $"{SelectedAccount.Name}: {needed:C} still needed for {SelectedAccountFundingBills.Count} upcoming bill{(SelectedAccountFundingBills.Count == 1 ? "" : "s")}.";
        }
    }

    private BillRow? _selectedBill;
    public BillRow? SelectedBill { get => _selectedBill; set => SetProperty(ref _selectedBill, value); }

    private int _billCalendarMonthOffset;
    public int BillCalendarMonthOffset
    {
        get => _billCalendarMonthOffset;
        private set
        {
            if (SetProperty(ref _billCalendarMonthOffset, value))
            {
                OnPropertyChanged(nameof(BillCalendarMonthTitle));
            }
        }
    }

    public string BillCalendarMonthTitle => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
        .AddMonths(BillCalendarMonthOffset)
        .ToString("MMMM yyyy");

    public string BillCalendarTitle
    {
        get
        {
            if (SummaryPeriod == "All")
            {
                return "Upcoming bills";
            }

            if (SummaryPeriod == "Weekly")
            {
                return "Week view";
            }

            var (start, _) = GetSummaryPeriodRange();
            return start.ToString("MMMM yyyy");
        }
    }

    public string BillCalendarSubtitle
    {
        get
        {
            if (SummaryPeriod == "All")
            {
                return "Showing the current month while transactions are unfiltered.";
            }

            var (start, end) = GetSummaryPeriodRange();
            var periodLabel = SummaryPeriod == "Weekly" ? "Active week" : "Active month";
            return $"{periodLabel}: {start:dd MMM} - {end:dd MMM yyyy}";
        }
    }

    public bool IsMonthlyCalendar => SummaryPeriod != "Weekly";
    public decimal BillCalendarVisibleTotal => BillCalendarDays.Sum(d => d.Total);
    public int BillCalendarBillCount => BillCalendarDays.Sum(d => d.Bills.Count);
    public int BillCalendarPaidCount => BillCalendarDays.Sum(d => d.Bills.Count(b => b.IsPaid));
    public int BillCalendarUnpaidCount => BillCalendarBillCount - BillCalendarPaidCount;
    public string BillCalendarModeSummary => SummaryPeriod == "Weekly"
        ? "Showing one week only."
        : "Showing the full selected month.";

    public IReadOnlyList<string> BillCalendarDayHeaders
    {
        get
        {
            if (SummaryPeriod == "Weekly" && BillCalendarDays.Count == 7)
            {
                return BillCalendarDays.Select(d => d.Date.ToString("ddd")).ToList();
            }
            return ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        }
    }

    private CategoryRow? _selectedCategory;
    public CategoryRow? SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }

    private DebtRow? _selectedDebt;
    public DebtRow? SelectedDebt { get => _selectedDebt; set => SetProperty(ref _selectedDebt, value); }

    private SavingsGoalRow? _selectedSavingsGoal;
    public SavingsGoalRow? SelectedSavingsGoal { get => _selectedSavingsGoal; set => SetProperty(ref _selectedSavingsGoal, value); }

    private RecurringPaymentRow? _selectedRecurringPayment;
    public RecurringPaymentRow? SelectedRecurringPayment { get => _selectedRecurringPayment; set => SetProperty(ref _selectedRecurringPayment, value); }

    private string _selectedNavSection = "Dashboard";
    private int _selectedPlanningTabIndex;
    public string SelectedNavSection
    {
        get => _selectedNavSection;
        set
        {
            if (SetProperty(ref _selectedNavSection, value))
            {
                SelectedPlanningTabIndex = value switch
                {
                    "Bills" => 1,
                    "Subscriptions" => 2,
                    "Calendar" => 3,
                    "Reports" => 4,
                    "Budget" => 5,
                    "Debts" => 6,
                    "Tools" => 7,
                    "Goals" => 8,
                    "Daily" => 9,
                    _ => 0
                };
                OnPropertyChanged(nameof(IsDashboardSection));
                OnPropertyChanged(nameof(IsTransactionsSection));
                OnPropertyChanged(nameof(IsCategoriesSection));
                OnPropertyChanged(nameof(IsPlanningSection));
                OnPropertyChanged(nameof(IsPlanningHomeSection));
                OnPropertyChanged(nameof(IsBillsSection));
                OnPropertyChanged(nameof(IsSubscriptionsSection));
                OnPropertyChanged(nameof(IsCalendarSection));
                OnPropertyChanged(nameof(IsReportsSection));
                OnPropertyChanged(nameof(IsBudgetSection));
                OnPropertyChanged(nameof(IsDebtsSection));
                OnPropertyChanged(nameof(IsToolsSection));
                OnPropertyChanged(nameof(IsGoalsSection));
                OnPropertyChanged(nameof(IsDailySection));
            }
        }
    }

    public int SelectedPlanningTabIndex { get => _selectedPlanningTabIndex; set => SetProperty(ref _selectedPlanningTabIndex, value); }
    public bool IsDashboardSection => _selectedNavSection == "Dashboard";
    public bool IsTransactionsSection => _selectedNavSection == "Transactions";
    public bool IsCategoriesSection => _selectedNavSection == "Categories";
    public bool IsPlanningSection => _selectedNavSection is "Planning" or "Bills" or "Subscriptions" or "Calendar" or "Reports" or "Budget" or "Debts" or "Tools" or "Goals" or "Daily";
    public bool IsPlanningHomeSection => _selectedNavSection == "Planning";
    public bool IsBillsSection => _selectedNavSection == "Bills";
    public bool IsSubscriptionsSection => _selectedNavSection == "Subscriptions";
    public bool IsCalendarSection => _selectedNavSection == "Calendar";
    public bool IsReportsSection => _selectedNavSection == "Reports";
    public bool IsBudgetSection => _selectedNavSection == "Budget";
    public bool IsDebtsSection => _selectedNavSection == "Debts";
    public bool IsToolsSection => _selectedNavSection == "Tools";
    public bool IsGoalsSection => _selectedNavSection == "Goals";
    public bool IsDailySection => _selectedNavSection == "Daily";

    public MainViewModel()
    {
    }

    public void LoadDashboard()
    {
        RolloverPastAccountTargets();
        LoadSettings();
        LoadCategories();
        LoadDebts();
        LoadSavingsGoals();
        LoadAccounts();
        LoadBudgetSnapshots();
        LoadTransactionRules();
        LoadCategoryLimits();
        LoadTransactions(refreshInsights: false, refreshRecurring: false, refreshDependentViews: false);
        LoadBudget(loadInsights: false);
        ApplyBillAutopayMatches();
        LoadBills();
        LoadCashForecast();
        LoadDebtPaymentAudit();
        LoadDangerAlerts();
        LoadDailyTracker();
        LoadDashboardWidgets();
    }

    public void SetNextPayDate(DateTime nextPayDate)
    {
        using var db = new FinoraDbContext();

        var setting = db.AppSettings.FirstOrDefault(s => s.Key == NextPayDateSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = NextPayDateSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = nextPayDate.Date.ToString("O");
        db.SaveChanges();

        NextPayDate = nextPayDate.Date;
        LoadAccounts();
        LoadTransactions(refreshInsights: false, refreshRecurring: false);
        LoadBudget(loadInsights: false);
    }

    private void LoadSettings()
    {
        using var db = new FinoraDbContext();

        var nextPayDateValue = db.AppSettings
            .Where(s => s.Key == NextPayDateSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault();

        NextPayDate = DateTime.TryParse(nextPayDateValue, out var nextPayDate)
            ? nextPayDate.Date
            : DateTime.Today;

        var summaryPeriod = db.AppSettings
            .Where(s => s.Key == SummaryPeriodSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault();

        _summaryPeriod = summaryPeriod is "All" or "Weekly" or "Monthly"
            ? summaryPeriod
            : "All";
        OnPropertyChanged(nameof(SummaryPeriod));

        var debtPaymentPeriod = db.AppSettings
            .Where(s => s.Key == DebtPaymentPeriodSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault();

        _debtPaymentPeriod = debtPaymentPeriod is "Weekly" or "Fortnightly" or "Monthly"
            ? debtPaymentPeriod
            : "Weekly";
        OnPropertyChanged(nameof(DebtPaymentPeriod));

        var affordabilityAmountValue = db.AppSettings.Where(s => s.Key == AffordabilityAmountSettingKey).Select(s => s.Value).FirstOrDefault();
        if (decimal.TryParse(affordabilityAmountValue, out var savedAmount) && savedAmount > 0)
            _affordabilityAmount = savedAmount;

        var affordabilityWeeksValue = db.AppSettings.Where(s => s.Key == AffordabilityWeeksSettingKey).Select(s => s.Value).FirstOrDefault();
        if (int.TryParse(affordabilityWeeksValue, out var savedWeeks) && savedWeeks > 0)
            _affordabilityWeeks = savedWeeks;

        var affordabilitySafetyBufferValue = db.AppSettings.Where(s => s.Key == AffordabilitySafetyBufferSettingKey).Select(s => s.Value).FirstOrDefault();
        if (decimal.TryParse(affordabilitySafetyBufferValue, out var savedBuffer) && savedBuffer >= 0)
            _affordabilitySafetyBuffer = savedBuffer;

        var affordabilityAccountNameValue = db.AppSettings.Where(s => s.Key == AffordabilityAccountNameSettingKey).Select(s => s.Value).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(affordabilityAccountNameValue))
            _affordabilityAccountName = affordabilityAccountNameValue;

        var emergencyFundAccountNameValue = db.AppSettings.Where(s => s.Key == EmergencyFundAccountNameSettingKey).Select(s => s.Value).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(emergencyFundAccountNameValue))
            _emergencyFundAccountName = emergencyFundAccountNameValue;

        var showAllTransactionsValue = db.AppSettings.Where(s => s.Key == ShowAllTransactionsSettingKey).Select(s => s.Value).FirstOrDefault();
        _showAllTransactions = showAllTransactionsValue == "true";

        var savingsRecommendationDeclinedValue = db.AppSettings.Where(s => s.Key == SavingsBudgetRecommendationDeclinedSettingKey).Select(s => s.Value).FirstOrDefault();
        _isSavingsRecommendationDeclined = savingsRecommendationDeclinedValue == "true";

        var ignoredJson = db.AppSettings.Where(s => s.Key == IgnoredSubscriptionsSettingKey).Select(s => s.Value).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(ignoredJson))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(ignoredJson);
                if (list is not null)
                {
                    _ignoredSubscriptions.Clear();
                    foreach (var item in list)
                        _ignoredSubscriptions.Add(item);
                }
            }
            catch { }
        }

        OnPropertyChanged(nameof(AffordabilityAmount));
        OnPropertyChanged(nameof(AffordabilityWeeks));
        OnPropertyChanged(nameof(AffordabilitySafetyBuffer));
        OnPropertyChanged(nameof(AffordabilityAccountName));
        OnPropertyChanged(nameof(EmergencyFundAccountName));
        OnPropertyChanged(nameof(ShowAllTransactions));
        OnPropertyChanged(nameof(ShowSavingsRecommendation));
    }

    private void SaveAffordabilitySettings()
    {
        using var db = new FinoraDbContext();
        UpsertSetting(db, AffordabilityAmountSettingKey, _affordabilityAmount.ToString());
        UpsertSetting(db, AffordabilityWeeksSettingKey, _affordabilityWeeks.ToString());
        UpsertSetting(db, AffordabilitySafetyBufferSettingKey, _affordabilitySafetyBuffer.ToString());
        UpsertSetting(db, AffordabilityAccountNameSettingKey, _affordabilityAccountName);
        db.SaveChanges();
    }

    private void SaveEmergencyFundAccountSetting()
    {
        using var db = new FinoraDbContext();
        UpsertSetting(db, EmergencyFundAccountNameSettingKey, _emergencyFundAccountName);
        db.SaveChanges();
    }

    private static void UpsertSetting(FinoraDbContext db, string key, string value)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting is null)
        {
            setting = new AppSetting { Key = key };
            db.AppSettings.Add(setting);
        }
        setting.Value = value;
    }

    private static void SaveSummaryPeriod(string summaryPeriod)
    {
        if (summaryPeriod is not ("All" or "Weekly" or "Monthly"))
        {
            return;
        }

        using var db = new FinoraDbContext();
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == SummaryPeriodSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = SummaryPeriodSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = summaryPeriod;
        db.SaveChanges();
    }

    private static void SaveDebtPaymentPeriod(string debtPaymentPeriod)
    {
        if (debtPaymentPeriod is not ("Weekly" or "Fortnightly" or "Monthly"))
        {
            return;
        }

        using var db = new FinoraDbContext();
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == DebtPaymentPeriodSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = DebtPaymentPeriodSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = debtPaymentPeriod;
        db.SaveChanges();
    }

    public void LoadTransactions(bool refreshInsights = true, bool refreshRecurring = true, bool refreshDependentViews = true)
    {
        Transactions.Clear();

        using var db = new FinoraDbContext();

        var allTransactions = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToList();

        var (periodStart, periodEnd) = GetSummaryPeriodRange();
        List<Transaction> periodTransactions;
        if (SummaryPeriod == "All")
        {
            TransactionTotalCount = allTransactions.Count;
            var cutoff = DateTime.Today.AddMonths(-6);
            periodTransactions = _showAllTransactions
                ? allTransactions
                : allTransactions.Where(t => t.Date.Date >= cutoff.Date).ToList();
            TransactionShownCount = periodTransactions.Count;
        }
        else
        {
            periodTransactions = allTransactions
                .Where(t => t.Date.Date >= periodStart.Date && t.Date.Date <= periodEnd.Date)
                .ToList();
            TransactionTotalCount = periodTransactions.Count;
            TransactionShownCount = periodTransactions.Count;
        }
        OnPropertyChanged(nameof(HasHiddenTransactions));
        OnPropertyChanged(nameof(TransactionCountSummary));

        var transactions = periodTransactions;

        LoadCoverTransferGroups(periodTransactions);

        if (HideCoverTransfers)
        {
            transactions = transactions
                .Where(t => !IsCoverTransfer(t))
                .ToList();
        }

        if (ShowUncategorisedOnly)
        {
            transactions = transactions.Where(IsUncategorisedTransaction).ToList();
        }
        else
        {
            transactions = TransactionTypeFilter switch
            {
                "Spending" => transactions.Where(IsSpendingTransaction).ToList(),
                "Income" => transactions.Where(IsIncomeTransaction).ToList(),
                "Transfers" => transactions.Where(TransactionClassification.IsInternalMovement).ToList(),
                _ => transactions
            };
        }

        if (!string.IsNullOrWhiteSpace(TransactionSearch))
        {
            var search = TransactionSearch.Trim();
            transactions = transactions
                .Where(t =>
                    t.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (t.Account?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    GetDisplayCategoryName(t).Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var t in transactions)
        {
            Transactions.Add(new TransactionRow
            {
                Id = t.Id,
                Date = t.Date,
                Description = t.Description,
                Amount = t.AmountDollars,
                AccountName = t.Account?.Name ?? "",
                CategoryName = GetDisplayCategoryName(t),
                CoverPair = GetCoverPairLabel(t),
                TransferId = t.TransferId
            });
        }

        PeriodSpending = Math.Abs(periodTransactions.Where(IsSpendingTransaction).Sum(t => t.AmountDollars));
        PeriodIncome = periodTransactions.Where(IsIncomeTransaction).Sum(t => t.AmountDollars);
        PeriodNetCashFlow = RoundSignedDollars(PeriodIncome - PeriodSpending);
        var periodDays = SummaryPeriod == "All"
            ? Math.Max((periodTransactions.Count == 0 ? 1 : (periodTransactions.Max(t => t.Date.Date) - periodTransactions.Min(t => t.Date.Date)).Days + 1), 1)
            : Math.Max((periodEnd.Date - periodStart.Date).Days + 1, 1);
        AverageDailySpending = RoundDollars(PeriodSpending / periodDays);
        // Use budgeted savings / weekly income when a budget is configured —
        // the transaction-based formula (income − spending) / income inflates
        // the rate because transfers to bill savers aren't counted as spending.
        // Otherwise, only count money that actually moved into Savings-type accounts —
        // transfers into Bills-type sinking funds (Wifi, Subscriptions, etc.) are not "savings".
        var transfersToSavings = periodTransactions
            .Where(t => t.AmountDollars > 0
                && t.Account?.Type == AccountType.Savings
                && TransactionClassification.IsInternalMovement(t))
            .Sum(t => t.AmountDollars);
        SavingsRate = WeeklyIncome > 0 && BudgetSavings > 0
            ? Math.Round(Math.Min(BudgetSavings / WeeklyIncome, 1m), 4)
            : PeriodIncome <= 0 ? 0
            : Math.Round(Math.Min(transfersToSavings / PeriodIncome, 1m), 4);
        SavingsAmountThisPeriod = transfersToSavings;

        // Independent of SummaryPeriod — always the current pay week, since
        // SafeToSpendAmount is a weekly figure.
        var currentWeekStart = GetCurrentPayWeekStart();
        var currentWeekEnd = currentWeekStart.AddDays(6);
        SavingsTransfersThisWeek = allTransactions
            .Where(t => t.Date.Date >= currentWeekStart && t.Date.Date <= currentWeekEnd
                && t.AmountDollars > 0
                && t.Account?.Type == AccountType.Savings
                && TransactionClassification.IsInternalMovement(t))
            .Sum(t => t.AmountDollars);
        OnPropertyChanged(nameof(CashRunwaySummary));
        var largestCategory = periodTransactions
            .Where(IsSpendingTransaction)
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .Select(g => new { Name = g.Key, Amount = Math.Abs(g.Sum(t => t.AmountDollars)) })
            .OrderByDescending(g => g.Amount)
            .FirstOrDefault();
        LargestSpendingCategory = largestCategory?.Name ?? "None";
        LargestSpendingCategoryAmount = largestCategory?.Amount ?? 0;
        PeriodBillsDue = SummaryPeriod == "All"
            ? 0
            : GetVisibleBillOccurrences(db, db.Bills.ToList(), periodStart, periodEnd)
                .Sum(o => o.Bill.AmountDollars);
        OnPropertyChanged(nameof(SummaryDateRange));
        LoadBudgetVariance(periodTransactions);
        LoadMonthlyTrend(allTransactions);
        LoadInsightsCategoryChart(periodTransactions);
        if (refreshDependentViews)
        {
            LoadDailyTracker();
        }
        RefreshNetWorth();
        ComputeFinancialHealthScore(allTransactions);
        ComputeBudgetStreak(allTransactions);
        if (refreshDependentViews)
        {
            LoadCashForecast();
            LoadBills();
            LoadReports(allTransactions, refreshRecurring);
        }
        if (refreshInsights)
        {
            LoadInsights();
        }
    }

    private static string GetDisplayCategoryName(Transaction transaction)
    {
        return TransactionClassification.IsInternalMovement(transaction)
            ? "Transfer"
            : GetDisplayCategoryName(transaction.Category?.Name);
    }

    private static string GetDisplayCategoryName(string? categoryName)
    {
        return string.IsNullOrWhiteSpace(categoryName) || categoryName == "Unplanned"
            ? "Misc"
            : categoryName;
    }

    private static bool IsCoverTransfer(Transaction transaction)
    {
        return transaction.Description.StartsWith("Cover from ", StringComparison.OrdinalIgnoreCase) ||
            transaction.Description.StartsWith("Cover to ", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCoverPairLabel(Transaction transaction)
    {
        if (transaction.Description.StartsWith("Cover from ", StringComparison.OrdinalIgnoreCase))
        {
            return "Cover pair";
        }

        if (transaction.Description.StartsWith("Cover to ", StringComparison.OrdinalIgnoreCase))
        {
            return "Cover pair";
        }

        return transaction.TransferId is { } transferId && transferId != Guid.Empty
            ? "Linked transfer"
            : string.Empty;
    }

    private void LoadCoverTransferGroups(IEnumerable<Transaction> transactions)
    {
        CoverTransferGroups.Clear();
        var coverRows = transactions
            .Where(IsCoverTransfer)
            .Select(t => new
            {
                t.Date,
                Amount = Math.Abs(t.AmountDollars),
                AccountName = t.Account?.Name ?? "",
                t.Description
            })
            .ToList();

        foreach (var group in coverRows
            .GroupBy(t => new { t.Date.Date, t.Amount })
            .OrderByDescending(g => g.Key.Date)
            .ThenByDescending(g => g.Key.Amount))
        {
            var from = group.FirstOrDefault(t => t.Description.StartsWith("Cover from ", StringComparison.OrdinalIgnoreCase));
            var to = group.FirstOrDefault(t => t.Description.StartsWith("Cover to ", StringComparison.OrdinalIgnoreCase));
            if (from is null || to is null)
            {
                continue;
            }

            CoverTransferGroups.Add(new CoverTransferGroupRow
            {
                Date = group.Key.Date,
                Amount = group.Key.Amount,
                FromAccount = from.Description["Cover from ".Length..].Trim(),
                ToAccount = to.Description["Cover to ".Length..].Trim()
            });
        }
    }

    private static bool IsSpendingTransaction(Transaction transaction)
    {
        return transaction.AmountDollars < 0 && !TransactionClassification.IsInternalMovement(transaction);
    }

    private static bool IsIncomeTransaction(Transaction transaction)
    {
        return transaction.AmountDollars > 0 &&
            transaction.Category?.Type == CategoryType.Income &&
            !TransactionClassification.IsInternalMovement(transaction);
    }

    private static bool IsUncategorisedTransaction(Transaction transaction)
    {
        return transaction.AmountCents < 0 &&
            !TransactionClassification.IsInternalMovement(transaction) &&
            (transaction.Category is null || transaction.Category.Name is "Misc" or "Unplanned");
    }

    public void ToggleUncategorisedTransactions()
    {
        TransactionTypeFilter = "All";
        ShowUncategorisedOnly = !ShowUncategorisedOnly;
        OnPropertyChanged(nameof(TransactionFilterLabel));
    }

    public void ShowAllTransactionTypes()
    {
        ShowUncategorisedOnly = false;
        TransactionTypeFilter = "All";
        LoadTransactions();
    }

    public void ShowSpendingTransactions()
    {
        ShowUncategorisedOnly = false;
        TransactionTypeFilter = "Spending";
        LoadTransactions();
    }

    public void ShowIncomeTransactions()
    {
        ShowUncategorisedOnly = false;
        TransactionTypeFilter = "Income";
        LoadTransactions();
    }

    public void ShowTransferTransactions()
    {
        ShowUncategorisedOnly = false;
        TransactionTypeFilter = "Transfers";
        LoadTransactions();
    }

    public void ToggleCoverTransfers()
    {
        HideCoverTransfers = !HideCoverTransfers;
    }

    // True when the period makes a budget comparison meaningful.
    public bool ShowBudgetVarianceTable => SummaryPeriod != "All";
    public string BudgetVariancePeriodLabel => SummaryPeriod == "Weekly" ? "this week" : "this month";
    public string BudgetVarianceBudgetHeader => SummaryPeriod == "Weekly" ? "Budget (wk)" : "Budget (mo)";
    public string BudgetVarianceActualHeader => SummaryPeriod == "Weekly" ? "Spent (wk)" : "Spent (mo)";

    private void LoadBudgetVariance(IReadOnlyList<Transaction> periodTransactions)
    {
        BudgetVarianceRows.Clear();
        OnPropertyChanged(nameof(ShowBudgetVarianceTable));
        OnPropertyChanged(nameof(BudgetVariancePeriodLabel));
        OnPropertyChanged(nameof(BudgetVarianceBudgetHeader));
        OnPropertyChanged(nameof(BudgetVarianceActualHeader));

        if (SummaryPeriod == "All")
            return; // "All" period is shown via a note in the XAML instead

        // Scale weekly budget to the current period so both columns are in the same unit.
        var (ps, pe) = GetSummaryPeriodRange();
        var periodWeeks = Math.Max((decimal)(pe.Date - ps.Date).TotalDays / 7m, 1m);
        decimal PeriodBudget(decimal weekly) => RoundDollars(weekly * periodWeeks);

        var actualBills = SummaryPeriod == "Weekly"
            ? PeriodBillsDue
            : GetActualSpending(periodTransactions, IsBillCategory);

        // Savings transfers are internal movements — never counted as "spending" — so
        // this row is omitted; savings are tracked via account balances instead.
        var rows = new[]
        {
            new BudgetVarianceRow { Category = "Bills",      Budgeted = PeriodBudget(BudgetBills),      Actual = actualBills },
            new BudgetVarianceRow { Category = "Essentials", Budgeted = PeriodBudget(BudgetEssentials), Actual = GetActualSpending(periodTransactions, IsEssentialCategory) },
            new BudgetVarianceRow { Category = "Unplanned",  Budgeted = PeriodBudget(BudgetUnplanned),  Actual = GetActualSpending(periodTransactions, name => !IsBillCategory(name) && !IsEssentialCategory(name)) }
        };

        foreach (var row in rows)
        {
            BudgetVarianceRows.Add(row);
        }
    }

    private static decimal GetActualSpending(IEnumerable<Transaction> transactions, Func<string?, bool> categoryMatch)
    {
        return RoundDollars(transactions
            .Where(IsSpendingTransaction)
            .Where(t => categoryMatch(t.Category?.Name))
            .Sum(t => Math.Abs(t.AmountDollars)));
    }

    public void LoadCashForecast()
    {
        CashForecastRows.Clear();
        CashForecastChartRows.Clear();
        AccountProjections.Clear();
        using var db = new FinoraDbContext();
        var balanceCents = db.Accounts
            .AsNoTracking()
            .Include(a => a.Transactions)
            .Where(a => a.Type != AccountType.Credit)
            .SelectMany(a => a.Transactions)
            .Sum(t => t.AmountCents);
        var balance = balanceCents / 100m;
        var forecastDays = ForecastRange switch
        {
            "8 weeks" => 56,
            "3 months" => 92,
            _ => 42
        };
        var forecastEnd = new[] { DateTime.Today.AddDays(forecastDays), NextPayDate.Date.AddDays(28) }.Max();
        var events = new List<(DateTime Date, string Description, decimal Change)>();

        var bills = db.Bills.AsNoTracking().Include(b => b.Account).ToList();
        foreach (var occurrence in GetVisibleBillOccurrences(db, bills, DateTime.Today, forecastEnd)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date)))
        {
            events.Add((occurrence.Date, $"{occurrence.Bill.Name} bill", -occurrence.Bill.AmountDollars));
        }

        if (WeeklyIncome > 0)
        {
            var payDate = NextPayDate.Date;
            while (payDate < DateTime.Today)
            {
                payDate = payDate.AddDays(7);
            }

            while (payDate <= forecastEnd)
            {
                events.Add((payDate, "Expected income", WeeklyIncome));
                payDate = payDate.AddDays(7);
            }
        }

        foreach (var item in events.OrderBy(e => e.Date).ThenBy(e => e.Change))
        {
            balance += item.Change;
            CashForecastRows.Add(new CashForecastRow
            {
                Date = item.Date,
                Description = item.Description,
                Change = item.Change,
                ProjectedBalance = RoundSignedDollars(balance)
            });
        }

        LoadAccountProjections(db, bills, forecastEnd);
        LoadCashForecastChartRows();
        RefreshForecastSummaries();
        LoadDangerAlerts();
        LoadDashboardWidgets();
    }

    private void LoadCashForecastChartRows()
    {
        CashForecastChartRows.Clear();
        if (CashForecastRows.Count == 0)
        {
            return;
        }

        var grouped = CashForecastRows
            .GroupBy(r => r.Date.Date)
            .Select(g => new { Date = g.Key, Balance = g.Last().ProjectedBalance })
            .OrderBy(r => r.Date)
            .ToList();
        var min = grouped.Min(r => r.Balance);
        var max = grouped.Max(r => r.Balance);
        var range = Math.Max(max - min, 1);

        for (var i = 0; i < grouped.Count; i++)
        {
            var row = grouped[i];
            var prevBalance = i == 0 ? row.Balance : grouped[i - 1].Balance;
            CashForecastChartRows.Add(new CashForecastChartRow
            {
                Date = row.Date,
                ProjectedBalance = row.Balance,
                Share = Math.Clamp((double)((row.Balance - min) / range), 0.05, 1),
                NetChange = RoundDollars(row.Balance - prevBalance)
            });
        }
    }

    private void RefreshForecastSummaries()
    {
        OnPropertyChanged(nameof(ForecastLowPoint));
        OnPropertyChanged(nameof(ForecastEndPoint));
        OnPropertyChanged(nameof(ForecastLowPointSummary));
        OnPropertyChanged(nameof(ForecastEndBalanceSummary));
        OnPropertyChanged(nameof(ForecastBillsTotal));
        OnPropertyChanged(nameof(ForecastIncomeTotal));
        OnPropertyChanged(nameof(ForecastEventSummary));
        OnPropertyChanged(nameof(UpcomingSqueezeSummary));
    }

    private void LoadAccountProjections(FinoraDbContext db, List<Bill> bills, DateTime forecastEnd)
    {
        if (_allAccountRows.Count == 0) return;

        var nextPay = NextPayDate.Date;
        while (nextPay < DateTime.Today) nextPay = nextPay.AddDays(7);

        var beforePayOccurrences = GetVisibleBillOccurrences(db, bills, DateTime.Today, nextPay.AddDays(-1))
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .ToList();
        var afterPayOccurrences = GetVisibleBillOccurrences(db, bills, nextPay, forecastEnd)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .ToList();

        decimal totalIncomeForecast = 0;
        if (WeeklyIncome > 0)
        {
            var payDate = nextPay;
            while (payDate <= forecastEnd) { totalIncomeForecast += WeeklyIncome; payDate = payDate.AddDays(7); }
        }

        var incomeAccountName = string.IsNullOrWhiteSpace(AffordabilityAccountName)
            ? _allAccountRows.FirstOrDefault(a => a.Type == AccountType.Spending.ToString())?.Name ?? string.Empty
            : AffordabilityAccountName;

        var nonCreditAccounts = _allAccountRows.Where(a => a.Type != AccountType.Credit.ToString()).ToList();

        foreach (var account in nonCreditAccounts)
        {
            var billsBefore = beforePayOccurrences
                .Where(o => o.Bill.AccountId == account.Id)
                .Sum(o => o.Bill.AmountDollars);
            var billsAfter = afterPayOccurrences
                .Where(o => o.Bill.AccountId == account.Id)
                .Sum(o => o.Bill.AmountDollars);
            var isIncomeAccount = string.Equals(account.Name, incomeAccountName, StringComparison.OrdinalIgnoreCase);

            AccountProjections.Add(new AccountProjectionRow
            {
                Name = account.Name,
                ColorHex = account.ColorHex,
                CurrentBalance = account.Balance,
                BillsBeforePay = billsBefore,
                BillsAfterPay = billsAfter,
                NextIncomeCredit = isIncomeAccount ? WeeklyIncome : 0,
                TotalIncomeCredit = isIncomeAccount ? totalIncomeForecast : 0,
            });
        }

        if (nonCreditAccounts.Count > 1)
        {
            AccountProjections.Add(new AccountProjectionRow
            {
                Name = "Total",
                ColorHex = "#64748B",
                IsTotal = true,
                CurrentBalance = nonCreditAccounts.Sum(a => a.Balance),
                BillsBeforePay = beforePayOccurrences.Sum(o => o.Bill.AmountDollars),
                BillsAfterPay = afterPayOccurrences.Sum(o => o.Bill.AmountDollars),
                NextIncomeCredit = WeeklyIncome,
                TotalIncomeCredit = totalIncomeForecast,
            });
        }
    }

    public int ApplyBillAutopayMatches()
    {
        using var db = new FinoraDbContext();
        // Tracked (no AsNoTracking) so DueDate advances are saved automatically.
        var bills = db.Bills
            .Include(b => b.Account)
            .ToList();
        var transactions = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.AmountCents < 0)
            .ToList();
        var applied = 0;

        foreach (var bill in bills)
        {
            var occurrences = GetVisibleBillOccurrences(db, new[] { bill }, DateTime.Today.AddDays(-45), DateTime.Today.AddDays(7));
            foreach (var occurrence in occurrences)
            {
                if (IsBillOccurrencePaid(db, bill.Id, occurrence.Date))
                {
                    continue;
                }

                var matches = transactions
                    .Where(t => IsBillPaymentMatch(t, bill, occurrence.Date))
                    .ToList();
                if (matches.Count != 1)
                {
                    continue;
                }

                var match = matches[0];
                var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == bill.Id && s.DueDate == occurrence.Date.Date);
                if (status is null)
                {
                    status = new BillOccurrenceStatus
                    {
                        BillId = bill.Id,
                        DueDate = occurrence.Date.Date
                    };
                    db.BillOccurrenceStatuses.Add(status);
                }

                RelabelBillAdjustment(db, match, bill, status);
                status.IsPaid = true;
                status.PaidOn = DateTime.Today;
                status.MatchedTransactionId = match.Id;
                status.MatchNote = BuildBillMatchReason(match, bill, occurrence.Date);

                // Auto-advance: keep the bill's base date pointing at the next expected occurrence.
                if (occurrence.Date.Date >= bill.DueDate.Date)
                {
                    bill.DueDate = GetNextBillDueDate(occurrence.Date.Date, bill.Frequency);
                }

                applied++;
            }
        }

        if (applied > 0)
        {
            db.SaveChanges();
            LoadBills();
            LoadAccounts();
        }

        return applied;
    }

    public int CleanupBillAdjustments()
    {
        return ApplyBillAutopayMatches();
    }

    public bool ApplyReviewedBillMatch(BillMatchReviewRow review)
    {
        using var db = new FinoraDbContext();
        var bill = db.Bills
            .Include(b => b.Account)
            .FirstOrDefault(b => b.Id == review.BillId);
        var transaction = db.Transactions
            .Include(t => t.Category)
            .FirstOrDefault(t => t.Id == review.TransactionId);
        if (bill is null || transaction is null)
        {
            return false;
        }

        var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == bill.Id && s.DueDate == review.DueDate.Date);
        if (status is null)
        {
            status = new BillOccurrenceStatus
            {
                BillId = bill.Id,
                DueDate = review.DueDate.Date
            };
            db.BillOccurrenceStatuses.Add(status);
        }

        RelabelBillAdjustment(db, transaction, bill, status);
        status.IsPaid = true;
        status.PaidOn = DateTime.Today;
        status.MatchedTransactionId = transaction.Id;
        status.MatchNote = review.Reason;

        // Auto-advance the bill's base due date so next occurrence shows automatically.
        if (review.DueDate.Date >= bill.DueDate.Date)
        {
            bill.DueDate = GetNextBillDueDate(review.DueDate.Date, bill.Frequency);
        }

        db.SaveChanges();
        LoadDashboard();
        return true;
    }

    public bool UndoLastBillCleanup()
    {
        using var db = new FinoraDbContext();
        var status = db.BillOccurrenceStatuses
            .Where(s => s.MatchedTransactionId != null && s.OriginalTransactionDescription != null)
            .OrderByDescending(s => s.PaidOn ?? s.DueDate)
            .FirstOrDefault();
        if (status?.MatchedTransactionId is null)
        {
            return false;
        }

        var transaction = db.Transactions.FirstOrDefault(t => t.Id == status.MatchedTransactionId.Value);
        if (transaction is null)
        {
            return false;
        }

        transaction.Description = status.OriginalTransactionDescription ?? transaction.Description;
        if (status.OriginalTransactionCategoryId is not null)
        {
            transaction.CategoryId = status.OriginalTransactionCategoryId.Value;
        }

        transaction.TransferId = Guid.TryParse(status.OriginalTransactionTransferId, out var transferId)
            ? transferId
            : null;

        status.IsPaid = false;
        status.PaidOn = null;
        status.MatchedTransactionId = null;
        status.MatchNote = string.Empty;
        status.OriginalTransactionDescription = null;
        status.OriginalTransactionCategoryId = null;
        status.OriginalTransactionTransferId = null;
        db.SaveChanges();
        LoadDashboard();
        return true;
    }

    /// <summary>
    /// Reverts a bill occurrence that was previously marked Paid or Skipped by mistake,
    /// restoring it to "due" — undoing any transaction relabeling and rolling the bill's
    /// due date back if it had auto-advanced past this occurrence.
    /// </summary>
    public bool MarkBillOccurrenceUnpaid(int billId, DateTime dueDate)
    {
        using var db = new FinoraDbContext();
        var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == billId && s.DueDate == dueDate.Date);
        if (status is null)
        {
            return false;
        }

        if (status.MatchedTransactionId is { } transactionId && status.OriginalTransactionDescription is not null)
        {
            var transaction = db.Transactions.FirstOrDefault(t => t.Id == transactionId);
            if (transaction is not null)
            {
                transaction.Description = status.OriginalTransactionDescription;
                if (status.OriginalTransactionCategoryId is not null)
                {
                    transaction.CategoryId = status.OriginalTransactionCategoryId.Value;
                }

                transaction.TransferId = Guid.TryParse(status.OriginalTransactionTransferId, out var transferId)
                    ? transferId
                    : null;
            }
        }

        var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is not null)
        {
            DebtPaymentMatcher.ApplyBillDebtPaymentStatus(db, bill, status.DueDate, false);

            // If marking this paid auto-advanced the bill's due date past this occurrence,
            // move it back so the occurrence becomes the current/upcoming one again.
            if (bill.DueDate.Date > status.DueDate.Date)
            {
                bill.DueDate = status.DueDate.Date;
            }
        }

        status.IsPaid = false;
        status.IsSkipped = false;
        status.PaidOn = null;
        status.MatchedTransactionId = null;
        status.MatchNote = string.Empty;
        status.OriginalTransactionDescription = null;
        status.OriginalTransactionCategoryId = null;
        status.OriginalTransactionTransferId = null;

        db.SaveChanges();
        RefreshAfterBillPaymentChange();
        return true;
    }

    private static void RelabelBillAdjustment(FinoraDbContext db, Transaction transaction, Bill bill, BillOccurrenceStatus status)
    {
        if (!transaction.Description.Equals("Up balance adjustment", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        status.OriginalTransactionDescription ??= transaction.Description;
        status.OriginalTransactionCategoryId ??= transaction.CategoryId;
        status.OriginalTransactionTransferId ??= transaction.TransferId?.ToString();
        transaction.Description = bill.Name;
        transaction.Category = GetBillAccountCategory(db, bill);
        transaction.TransferId = null;
    }

    private static Category GetBillAccountCategory(FinoraDbContext db, Bill bill)
    {
        var categoryName = bill.Account?.Name;
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            categoryName = "Misc";
        }

        var category = db.Categories.Local.FirstOrDefault(c => c.Name == categoryName) ??
            db.Categories.FirstOrDefault(c => c.Name == categoryName);
        if (category is not null)
        {
            return category;
        }

        category = new Category
        {
            Name = categoryName,
            Type = CategoryType.Expense
        };
        db.Categories.Add(category);
        return category;
    }

    private static string BuildBillMatchNote(IReadOnlyList<Transaction> matches, Bill bill, DateTime dueDate)
    {
        return matches.Count switch
        {
            0 => string.Empty,
            1 => BuildBillMatchReason(matches[0], bill, dueDate),
            _ => $"{matches.Count} possible matches; review before marking paid"
        };
    }

    private static string BuildBillMatchReason(Transaction transaction, Bill bill, DateTime dueDate)
    {
        var days = Math.Abs((transaction.Date.Date - dueDate.Date).TotalDays);
        var dateText = days == 0 ? "same day" : $"{days:0} day{(days == 1 ? "" : "s")} from due date";
        var billMerchantKey = TransactionClassification.GetMerchantKey(bill.Name);
        if (!string.IsNullOrWhiteSpace(billMerchantKey) &&
            TransactionClassification.GetMerchantKey(transaction.Description).Contains(billMerchantKey, StringComparison.OrdinalIgnoreCase))
        {
            return $"Matched by merchant, amount, and {dateText}";
        }

        if (transaction.AccountId == bill.AccountId && IsInternalBillAccountMovement(transaction))
        {
            return $"Matched by {bill.Account?.Name ?? "bill account"} account, amount, and {dateText}";
        }

        return $"Matched by amount and {dateText}";
    }

    private static bool IsBillPaymentMatch(Transaction transaction, Bill bill, DateTime dueDate)
    {
        if (Math.Abs(Math.Abs(transaction.AmountCents) - bill.AmountCents) > 1 ||
            Math.Abs((transaction.Date.Date - dueDate.Date).TotalDays) > 5)
        {
            return false;
        }

        var transactionMerchantKey = TransactionClassification.GetMerchantKey(transaction.Description);
        var billMerchantKey = TransactionClassification.GetMerchantKey(bill.Name);
        if (!string.IsNullOrWhiteSpace(billMerchantKey) &&
            transactionMerchantKey.Contains(billMerchantKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ruleKey = TransactionClassification.GetMerchantKey(bill.PaymentMatchText);
        if (!string.IsNullOrWhiteSpace(ruleKey) &&
            transactionMerchantKey.Contains(ruleKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return transaction.AccountId == bill.AccountId &&
            IsInternalBillAccountMovement(transaction);
    }

    private static bool IsInternalBillAccountMovement(Transaction transaction)
    {
        return transaction.Description.Equals("Up balance adjustment", StringComparison.OrdinalIgnoreCase) ||
            TransactionClassification.IsInternalMovementDescription(transaction.Description) ||
            TransactionClassification.IsInternalMovementCategory(transaction.Category?.Name);
    }

    public void PreviousWeeklyPage()
    {
        SummaryPeriod = "Weekly";
        WeeklyPageOffset--;
        LoadTransactions();
    }

    public void AllTransactions()
    {
        SummaryPeriod = "All";
        LoadTransactions();
    }

    public void LoadAllTransactions()
    {
        _showAllTransactions = true;
        OnPropertyChanged(nameof(ShowAllTransactions));
        SummaryPeriod = "All";
        LoadTransactions();
    }

    public void NextWeeklyPage()
    {
        SummaryPeriod = "Weekly";
        WeeklyPageOffset++;
        LoadTransactions();
    }

    public void CurrentWeeklyPage()
    {
        SummaryPeriod = "Weekly";
        WeeklyPageOffset = 0;
        LoadTransactions();
    }

    public void PreviousSummaryPage()
    {
        if (SummaryPeriod == "Weekly")
        {
            WeeklyPageOffset--;
        }
        else
        {
            MonthlyPageOffset++;
        }

        LoadTransactions();
    }

    public void NextSummaryPage()
    {
        if (SummaryPeriod == "Weekly")
        {
            WeeklyPageOffset++;
        }
        else
        {
            MonthlyPageOffset--;
        }

        LoadTransactions();
    }

    public void CurrentSummaryPage()
    {
        if (SummaryPeriod == "Weekly")
        {
            WeeklyPageOffset = 0;
        }
        else
        {
            MonthlyPageOffset = 0;
        }

        LoadTransactions();
    }

    private (DateTime Start, DateTime End) GetSummaryPeriodRange()
    {
        if (SummaryPeriod == "Weekly")
        {
            var start = GetCurrentPayWeekStart().AddDays(7 * WeeklyPageOffset);
            var end = start.AddDays(6);
            return (start, end);
        }

        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-MonthlyPageOffset);
        return (month, month.AddMonths(1).AddDays(-1));
    }

    private DateTime GetCurrentPayWeekStart()
    {
        var start = NextPayDate.Date;
        while (start > DateTime.Today)
        {
            start = start.AddDays(-7);
        }

        while (start.AddDays(6) < DateTime.Today)
        {
            start = start.AddDays(7);
        }

        return start;
    }

    /// <summary>
    /// Updates an ObservableCollection in place to match <paramref name="desired"/> using
    /// targeted Insert/Remove/Move operations instead of Clear+rebuild. Clearing would emit
    /// a Reset event that momentarily empties a bound ComboBox's Items, snapping its
    /// two-way bound SelectedItem to null and overwriting the underlying selection.
    /// </summary>
    private static void SyncObservableCollection(ObservableCollection<string> target, IReadOnlyList<string> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && target[i] == desired[i])
            {
                continue;
            }

            if (i < target.Count && target.Contains(desired[i]))
            {
                target.Move(target.IndexOf(desired[i]), i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }

    public void LoadAccounts()
    {
        Accounts.Clear();
        _allAccountRows.Clear();

        using var db = new FinoraDbContext();

        var bills = db.Bills
            .Include(b => b.Account)
            .ToList();
        var unpaidBillOccurrences = GetVisibleBillOccurrences(db, bills, DateTime.MinValue, NextPayDate)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .ToList();

        var accounts = db.Accounts
            .AsNoTracking()
            .Include(a => a.Transactions)
            .OrderBy(a => a.Name)
            .ToList();

        _allAccountRows.AddRange(accounts.Select(account =>
        {
            var balance = account.Transactions.Sum(t => t.AmountDollars);
            var billsBeforePay = unpaidBillOccurrences
                .Where(o => o.Bill.AccountId == account.Id)
                .Sum(o => o.Bill.AmountDollars);

            var accountBills = bills.Where(b => b.AccountId == account.Id).ToList();

            // If no bills are due before payday, look at the next single occurrence of
            // each bill so the card shows whether the account can cover its upcoming bills
            // (e.g. monthly bills due after payday still need to be funded now).
            var accountBillsDue = billsBeforePay;
            IReadOnlyList<(DateTime Date, Bill Bill)>? upcomingOccs = null;
            if (billsBeforePay == 0 && accountBills.Count > 0)
            {
                upcomingOccs = GetVisibleBillOccurrences(db, accountBills, DateTime.Today, DateTime.Today.AddDays(60))
                    .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
                    .GroupBy(o => o.Bill.Id)
                    .SelectMany(g => g.OrderBy(o => o.Date).Take(1))
                    .ToList();
                accountBillsDue = upcomingOccs.Sum(o => o.Bill.AmountDollars);
            }

            var neededNow = Math.Max(accountBillsDue - balance, 0);

            // How much should be saved by now toward this account's bill(s), based on each
            // bill's weekly pace through its current cycle — used to decide if the account
            // is genuinely behind rather than just short of the full upcoming bill amount.
            var expectedContribution = accountBills.Sum(b => GetExpectedBillContribution(b, DateTime.Today));

            // Find the next upcoming unpaid bill for this account (on or after today).
            var nextBill = unpaidBillOccurrences
                .Where(o => o.Bill.AccountId == account.Id && o.Date.Date >= DateTime.Today)
                .OrderBy(o => o.Date)
                .FirstOrDefault();
            if (nextBill.Bill is null && upcomingOccs is not null)
            {
                var first = upcomingOccs.OrderBy(o => o.Date).FirstOrDefault();
                if (first.Bill is not null) nextBill = first;
            }

            return new AccountRow
            {
                Id = account.Id,
                Name = account.Name,
                Type = account.Type.ToString(),
                Balance = balance,
                ColorHex = account.ColorHex,
                NeededNow = neededNow,
                BillsDue = accountBillsDue,
                ExpectedContribution = expectedContribution,
                Target = account.TargetDollars,
                TargetDate = account.TargetDate,
                TargetStartDate = account.TargetStartDate,
                TargetStartingBalance = account.TargetStartingBalanceDollars,
                NextPayDate = NextPayDate,
                NextUpcomingBillName   = nextBill.Bill?.Name,
                NextUpcomingBillAmount = nextBill.Bill?.AmountDollars ?? 0,
                NextUpcomingBillDate   = nextBill.Bill is not null ? nextBill.Date : (DateTime?)null,
            };
        }));

        ApplyAccountFilters();

        SyncObservableCollection(BudgetTransferAccountOptions, accounts
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Name)
            .Select(a => a.Name)
            .ToList());

        TotalBalance = _allAccountRows.Where(a => a.Type != AccountType.Credit.ToString()).Sum(a => a.Balance);
        BillsBalance = _allAccountRows.Where(a => a.Type == AccountType.Bills.ToString()).Sum(a => a.Balance);
        SavingsTotal = _allAccountRows.Where(a => a.Type == AccountType.Savings.ToString()).Sum(a => a.Balance);
        OnPropertyChanged(nameof(PrePaydayBalance));
        OnPropertyChanged(nameof(PostPaydayBalance));
        OnPropertyChanged(nameof(PrePaydayNegative));
        OnPropertyChanged(nameof(PrePaydayBalanceColor));
        RefreshNetWorth();
        LoadSelectedAccountFundingBills();
    }

    private void ApplyAccountFilters()
    {
        Accounts.Clear();
        var rows = _allAccountRows.ToList();
        if (!string.IsNullOrWhiteSpace(AccountSearchText))
        {
            var search = AccountSearchText.Trim();
            rows = rows
                .Where(a => a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            a.Type.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        rows = AccountSortOption switch
        {
            "Name" => rows.OrderBy(a => a.Name).ToList(),
            "Balance high" => rows.OrderByDescending(a => a.Balance).ThenBy(a => a.Name).ToList(),
            "Balance low" => rows.OrderBy(a => a.Balance).ThenBy(a => a.Name).ToList(),
            "Type" => rows.OrderBy(a => a.Type).ThenBy(a => a.Name).ToList(),
            "Target progress" => rows.OrderByDescending(a => a.TargetProgress).ThenBy(a => a.Name).ToList(),
            _ => rows.OrderByDescending(a => a.NeededNow).ThenBy(a => a.Name).ToList()
        };

        foreach (var row in rows)
        {
            Accounts.Add(row);
        }
    }

    private void LoadSelectedAccountFundingBills()
    {
        SelectedAccountFundingBills.Clear();
        if (SelectedAccount is null)
        {
            SelectedAccountFundingMessage = "Select an account to see its bills before payday.";
            OnPropertyChanged(nameof(SelectedAccountAuditSummary));
            return;
        }

        using var db = new FinoraDbContext();
        var account = db.Accounts
            .Include(a => a.Transactions)
            .FirstOrDefault(a => a.Id == SelectedAccount.Id);
        if (account is null)
        {
            SelectedAccountFundingMessage = "Account not found.";
            OnPropertyChanged(nameof(SelectedAccountAuditSummary));
            return;
        }

        var accountBalance = account.Transactions.Sum(t => t.AmountDollars);
        var bills = db.Bills
            .Where(b => b.AccountId == account.Id)
            .OrderBy(b => b.Name)
            .ToList();
        var unpaidOccurrences = GetVisibleBillOccurrences(db, bills, DateTime.MinValue, NextPayDate)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Bill.Name)
            .ToList();

        var runningAvailable = accountBalance;
        foreach (var occurrence in unpaidOccurrences)
        {
            var needed = Math.Max(occurrence.Bill.AmountDollars - runningAvailable, 0);
            runningAvailable -= occurrence.Bill.AmountDollars;

            SelectedAccountFundingBills.Add(new AccountFundingBillRow
            {
                Name = occurrence.Bill.Name,
                DueDate = occurrence.Date,
                Amount = occurrence.Bill.AmountDollars,
                NeededNow = needed,
                RemainingAfterBill = runningAvailable
            });
        }

        SelectedAccountFundingMessage = SelectedAccountFundingBills.Count == 0
            ? "No unpaid bills before payday for this account."
            : string.Empty;
        OnPropertyChanged(nameof(SelectedAccountAuditSummary));
    }

    public void LoadBills()
    {
        Bills.Clear();
        BillMatchReviews.Clear();
        BillPaymentHistory.Clear();

        using var db = new FinoraDbContext();

        var balances = db.Accounts
            .Include(a => a.Transactions)
            .ToDictionary(a => a.Id, a => a.Transactions.Sum(t => t.AmountDollars));

        var (periodStart, periodEnd) = GetSummaryPeriodRange();
        var billStart = SummaryPeriod == "Weekly" ? periodStart : periodStart;
        var billEnd = SummaryPeriod == "Weekly" ? (NextPayDate.Date > periodEnd.Date ? NextPayDate.Date : periodEnd) : periodEnd;
        var bills = db.Bills
            .Include(b => b.Account)
            .ToList();
        var billOccurrences = GetVisibleBillOccurrences(db, bills, billStart, billEnd)
            .OrderBy(o => o.Bill.IsPaid)
            .ThenBy(o => o.Date)
            .ThenBy(o => o.Bill.Name)
            .ToList();
        var billIds = billOccurrences.Select(o => o.Bill.Id).Distinct().ToList();
        var paidStatuses = db.BillOccurrenceStatuses
            .Where(s => billIds.Contains(s.BillId) && s.DueDate >= billStart.Date && s.DueDate <= billEnd.Date)
            .ToList()
            .ToDictionary(s => (s.BillId, s.DueDate.Date), s => s);
        var billPaymentTransactions = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.AmountCents < 0 && t.Date >= billStart.Date.AddDays(-5) && t.Date <= billEnd.Date.AddDays(5))
            .ToList();

        // Total unpaid bills per account — so NeededNow reflects ALL bills for the account,
        // not just one bill in isolation.
        var unpaidTotalsByAccount = billOccurrences
            .GroupBy(o => o.Bill.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(o =>
                {
                    var paid = paidStatuses.TryGetValue((o.Bill.Id, o.Date.Date), out var st) && (st?.IsPaid ?? false);
                    return paid ? 0m : o.Bill.AmountDollars;
                }));

        foreach (var occurrence in billOccurrences)
        {
            var bill = occurrence.Bill;
            var balance = balances.GetValueOrDefault(bill.AccountId);
            var isPaid = paidStatuses.TryGetValue((bill.Id, occurrence.Date.Date), out var status)
                ? status.IsPaid
                : false;
            var matches = billPaymentTransactions
                .Where(t => IsBillPaymentMatch(t, bill, occurrence.Date))
                .ToList();
            var matchNote = BuildBillMatchNote(matches, bill, occurrence.Date);

            Bills.Add(new BillRow
            {
                Id = bill.Id,
                Name = bill.Name,
                AccountName = bill.Account?.Name ?? "",
                Amount = bill.AmountDollars,
                DueDate = occurrence.Date,
                NextPayDate = NextPayDate,
                Frequency = bill.Frequency.ToString(),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                PeriodType = SummaryPeriod,
                AccountBalance = balance,
                NeededNow = isPaid ? 0 : Math.Max(unpaidTotalsByAccount.GetValueOrDefault(bill.AccountId) - balance, 0),
                IsAutoPay = bill.IsAutoPay,
                IsPaid = isPaid,
                MatchNote = status?.MatchNote ?? matchNote
            });

            if (!isPaid && matches.Count > 1)
            {
                foreach (var match in matches)
                {
                    BillMatchReviews.Add(new BillMatchReviewRow
                    {
                        BillId = bill.Id,
                        TransactionId = match.Id,
                        BillName = bill.Name,
                        AccountName = bill.Account?.Name ?? "",
                        DueDate = occurrence.Date,
                        TransactionDescription = match.Description,
                        TransactionDate = match.Date,
                        Amount = Math.Abs(match.AmountDollars),
                        Reason = BuildBillMatchReason(match, bill, occurrence.Date)
                    });
                }
            }
        }

        BillsOwedTotal = Bills.Where(b => !b.IsPaid).Sum(b => b.Amount);
        LoadBillPaymentHistory(db);
        LoadBillCalendar();
        LoadBillsDueNext7Days(db);
        OnPropertyChanged(nameof(BillsDueSoonCount));
        OnPropertyChanged(nameof(BillsNeedingFundingCount));
        OnPropertyChanged(nameof(BillsPaidThisPeriodCount));
        OnPropertyChanged(nameof(AmbiguousBillMatchCount));
        OnPropertyChanged(nameof(HasBillsDueNext7Days));
    }

    private void LoadBillsDueNext7Days(FinoraDbContext db)
    {
        BillsDueNext7Days.Clear();
        var today = DateTime.Today;
        var end = today.AddDays(7);
        var bills = db.Bills.AsNoTracking().Include(b => b.Account).ToList();
        var accountBalanceMap = _allAccountRows.ToDictionary(a => a.Name, a => a.Balance, StringComparer.OrdinalIgnoreCase);
        var upcoming = GetVisibleBillOccurrences(db, bills, today, end)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .OrderBy(o => o.Date)
            .ToList();
        foreach (var o in upcoming)
        {
            var daysUntil = (o.Date.Date - today).Days;
            var accountName = o.Bill.Account?.Name ?? "";
            var accountBalance = accountBalanceMap.TryGetValue(accountName, out var bal) ? bal : 0m;
            BillsDueNext7Days.Add(new BillDueSoonRow
            {
                Name = o.Bill.Name,
                AccountName = accountName,
                DueDate = o.Date,
                Amount = o.Bill.AmountDollars,
                DaysUntil = daysUntil,
                AccountBalance = accountBalance
            });
        }
    }

    private void LoadMonthlyTrend(IReadOnlyList<Transaction> allTransactions)
    {
        MonthlyTrend.Clear();
        var today = DateTime.Today;
        var rows = new List<MonthlyTrendRow>();
        for (var i = 5; i >= 0; i--)
        {
            var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var monthTx = allTransactions
                .Where(t => t.Date.Date >= monthStart && t.Date.Date <= monthEnd)
                .ToList();
            var income = monthTx.Where(IsIncomeTransaction).Sum(t => t.AmountDollars);
            var spending = Math.Abs(monthTx.Where(IsSpendingTransaction).Sum(t => t.AmountDollars));
            rows.Add(new MonthlyTrendRow
            {
                MonthLabel = monthStart.ToString("MMM"),
                Income = income,
                Spending = spending
            });
        }
        var maxValue = rows.Count == 0 ? 1m : Math.Max(rows.Max(r => Math.Max(r.Income, r.Spending)), 1m);
        foreach (var row in rows)
        {
            row.MaxValue = maxValue;
            MonthlyTrend.Add(row);
        }
    }

    private static readonly string[] CategoryColors =
    [
        "#6366F1", "#F59E0B", "#10B981", "#EF4444", "#8B5CF6", "#F472B6", "#14B8A6", "#F97316"
    ];

    private void LoadInsightsCategoryChart(IReadOnlyList<Transaction> periodTransactions)
    {
        InsightsCategoryRows.Clear();
        var groups = periodTransactions
            .Where(IsSpendingTransaction)
            .GroupBy(t => GetDisplayCategoryName(t))
            .Select(g => new { Name = g.Key, Amount = Math.Abs(g.Sum(t => t.AmountDollars)) })
            .OrderByDescending(g => g.Amount)
            .ToList();

        if (groups.Count == 0)
        {
            return;
        }

        var topGroups = groups.Take(6).ToList();
        var otherAmount = groups.Skip(6).Sum(g => g.Amount);
        var total = groups.Sum(g => g.Amount);
        if (total <= 0)
        {
            return;
        }

        for (var i = 0; i < topGroups.Count; i++)
        {
            var g = topGroups[i];
            InsightsCategoryRows.Add(new ReportChartRow
            {
                Label = g.Name,
                Amount = g.Amount,
                Share = (double)(g.Amount / total),
                ColorHex = CategoryColors[i % CategoryColors.Length]
            });
        }

        if (otherAmount > 0)
        {
            InsightsCategoryRows.Add(new ReportChartRow
            {
                Label = "Other",
                Amount = otherAmount,
                Share = (double)(otherAmount / total),
                ColorHex = "#475569"
            });
        }
    }

    private void RefreshNetWorth()
    {
        NetWorth = TotalBalance + SavingsTotal - DebtTotal;
    }

    private void ComputeFinancialHealthScore(IReadOnlyList<Transaction> allTransactions)
    {
        var budget = GetWeeklyBudget();

        // Emergency fund score (0-25): weeks of expenses covered by savings
        var monthlySpending = allTransactions
            .Where(t => t.Date.Date >= DateTime.Today.AddMonths(-3) && IsSpendingTransaction(t))
            .Sum(t => Math.Abs(t.AmountDollars));
        var avgWeeklySpending = monthlySpending / 13m;
        var hasSavingsAccount = _allAccountRows.Any(a => a.Type == AccountType.Savings.ToString());
        var emergencyBuffer = EmergencyFundAccount is not null
            ? Math.Max(EmergencyFundAccount.Balance, 0)
            : hasSavingsAccount ? SavingsTotal : Math.Max(TotalBalance, 0);
        var emergencyWeeks = avgWeeklySpending <= 0 ? 0 : emergencyBuffer / avgWeeklySpending;
        var emergencyScore = (int)Math.Min(emergencyWeeks / 12m * 25, 25);

        // Debt score (0-25): lower debt-to-income is better
        var weeklyIncome = budget?.IncomeDollars ?? 0;
        var monthlyIncome = weeklyIncome * 4;
        var debtToIncome = monthlyIncome <= 0 ? 1m : Math.Min(DebtTotal / (monthlyIncome * 12), 1m);
        var debtScore = (int)Math.Round((1 - debtToIncome) * 25);

        // Budget adherence score (0-25): % of budget rows not over-spent
        var adheringRows = BudgetVarianceRows.Count(r => r.PercentUsed <= 1.05m);
        var totalRows = BudgetVarianceRows.Count;
        var adherenceScore = totalRows == 0 ? 12 : (int)Math.Round((double)adheringRows / totalRows * 25);

        // Savings rate score (0-25)
        var savingsRateScore = (int)Math.Round((double)Math.Min(SavingsRate * 4, 1m) * 25);

        var total = emergencyScore + debtScore + adherenceScore + savingsRateScore;
        FinancialHealthScore = total;
        FinancialHealthGrade = total >= 90 ? "A" : total >= 75 ? "B" : total >= 60 ? "C" : total >= 45 ? "D" : "F";
        FinancialHealthColor = total >= 75 ? "#34D399" : total >= 50 ? "#FBBF24" : "#F87171";

        // Build component breakdown
        static string ScoreColor(int score, int max) =>
            score >= max ? "#34D399" : score >= max / 2 ? "#FBBF24" : "#F87171";

        FinancialHealthComponents.Clear();

        // Emergency fund
        var emergencyWeeksRounded = Math.Round(emergencyWeeks, 1);
        FinancialHealthComponents.Add(new FinancialHealthComponent
        {
            Name = "Emergency fund",
            Score = emergencyScore,
            MaxScore = 25,
            Detail = emergencyWeeks < 0.1m
                ? "No savings buffer detected"
                : EmergencyFundAccount is not null
                    ? $"{emergencyWeeksRounded} week{(emergencyWeeksRounded == 1 ? "" : "s")} of expenses covered by {EmergencyFundAccount.Name}"
                    : hasSavingsAccount
                        ? $"{emergencyWeeksRounded} week{(emergencyWeeksRounded == 1 ? "" : "s")} of expenses covered"
                        : $"{emergencyWeeksRounded} week{(emergencyWeeksRounded == 1 ? "" : "s")} of cash buffer covered",
            Tip = emergencyScore >= 25
                ? ""
                : $"Build a buffer to cover 12 weeks of expenses - need {Math.Max(12 - emergencyWeeks, 0) * avgWeeklySpending:C} more",
            ColorHex = ScoreColor(emergencyScore, 25),
            NavTarget = "Goals"
        });

        // Debt
        var debtToIncomePercent = Math.Round(debtToIncome * 100, 0);
        FinancialHealthComponents.Add(new FinancialHealthComponent
        {
            Name = "Debt",
            Score = debtScore,
            MaxScore = 25,
            Detail = DebtTotal <= 0
                ? "No debt — excellent"
                : $"{DebtTotal:C} total debt ({debtToIncomePercent}% of annual income)",
            Tip = debtScore >= 25
                ? ""
                : DebtTotal <= 0
                    ? ""
                    : "Pay down debt — aim for debt-to-annual-income below 10%",
            ColorHex = ScoreColor(debtScore, 25),
            NavTarget = "Debts"
        });

        // Budget adherence
        FinancialHealthComponents.Add(new FinancialHealthComponent
        {
            Name = "Budget",
            Score = adherenceScore,
            MaxScore = 25,
            Detail = totalRows == 0
                ? "No budget set up yet"
                : $"{adheringRows} of {totalRows} categories on track this period",
            Tip = adherenceScore >= 25
                ? ""
                : totalRows == 0
                    ? "Set up a weekly budget to start tracking adherence"
                    : $"{totalRows - adheringRows} categor{(totalRows - adheringRows == 1 ? "y" : "ies")} over budget — review spending to get all on track",
            ColorHex = ScoreColor(adherenceScore, 25),
            NavTarget = "Budget"
        });

        // Savings rate
        var savingsRatePercent = Math.Round(SavingsRate * 100, 1);
        var savingsPeriodLabel = SummaryPeriod == "Weekly" ? "this week" : SummaryPeriod == "All" ? "recently" : "this month";
        FinancialHealthComponents.Add(new FinancialHealthComponent
        {
            Name = "Savings rate",
            Score = savingsRateScore,
            MaxScore = 25,
            Detail = SavingsRate <= 0
                ? "Not saving from income currently"
                : $"{savingsRatePercent}% of income going to savings ({SavingsAmountThisPeriod:C} {savingsPeriodLabel})",
            Tip = savingsRateScore >= 25
                ? ""
                : SavingsRate <= 0
                    ? "Start saving — aim for 25% of income into savings/goals"
                    : $"Increase savings rate to 25%+ — currently {savingsRatePercent}%, need {25 - savingsRatePercent:0.#}% more",
            ColorHex = ScoreColor(savingsRateScore, 25),
            NavTarget = "Goals"
        });
    }

    private void ComputeBudgetStreak(IReadOnlyList<Transaction> allTransactions)
    {
        var budget = GetWeeklyBudget();

        // Prefer the dedicated Essentials+Unplanned allocation, but fall back to
        // whatever's left of income after bills/savings — covers budgets that only
        // have Income and Bills set, with no separate day-to-day spending split.
        var weekBudget = budget is null ? 0
            : budget.EssentialsDollars + budget.UnplannedDollars > 0
                ? budget.EssentialsDollars + budget.UnplannedDollars
                : Math.Max(budget.IncomeDollars - budget.BillsDollars - budget.SavingsDollars, 0);

        if (weekBudget <= 0)
        {
            BudgetStreakWeeks = 0;
            BudgetStreakDisplay = "Set a budget to track your streak.";
            return;
        }

        var payWeekStart = GetCurrentPayWeekStart();
        var streak = 0;

        for (var i = 1; i <= 26; i++)
        {
            var weekStart = payWeekStart.AddDays(-7 * i);
            var weekEnd = weekStart.AddDays(6);
            var weekSpending = allTransactions
                .Where(t => t.Date.Date >= weekStart && t.Date.Date <= weekEnd && IsSpendingTransaction(t))
                .Sum(t => Math.Abs(t.AmountDollars));

            if (weekSpending <= weekBudget)
            {
                streak++;
            }
            else
            {
                break;
            }
        }

        BudgetStreakWeeks = streak;
        BudgetStreakDisplay = streak == 0
            ? "Start your streak — stay under budget this week!"
            : streak == 1
                ? "1 week under budget"
                : $"{streak} weeks under budget";
    }

    public void LoadDailyTracker()
    {
        DailyTrackerDays.Clear();
        WeekCalendarDays.Clear();
        var today = DateTime.Today;
        var dow = (int)today.DayOfWeek; // 0=Sun
        var monday = today.AddDays(-(dow == 0 ? 6 : dow - 1));
        var rangeStart = monday;

        using var db = new FinoraDbContext();
        var transactions = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.Date >= rangeStart && t.Date <= today.AddDays(1).AddSeconds(-1))
            .OrderBy(t => t.Date)
            .AsNoTracking()
            .ToList();

        var rulesList = LoadTransactionRules(db);
        var allBillsForMatching = db.Bills.AsNoTracking().ToList();
        var categoryLimitMap = LoadCategoryLimitsFromDb(db)
            .ToDictionary(l => l.Category, l => l.WeeklyLimit, StringComparer.OrdinalIgnoreCase);

        var bills = db.Bills.Include(b => b.Account).ToList();
        var billOccurrences = GetVisibleBillOccurrences(db, bills, rangeStart, today)
            .OrderBy(o => o.Date)
            .ToList();
        var billIds = billOccurrences.Select(o => o.Bill.Id).Distinct().ToList();
        var paidStatuses = db.BillOccurrenceStatuses
            .Where(s => billIds.Contains(s.BillId) && s.DueDate >= rangeStart && s.DueDate <= today)
            .ToList()
            .ToDictionary(s => (s.BillId, s.DueDate.Date), s => s.IsPaid);

        WeekLabel = $"{monday:d MMM} – {monday.AddDays(6):d MMM yyyy}";

        for (var i = 0; i <= 6; i++)
        {
            var date = monday.AddDays(i);
            var dayRow = new DailyTrackerDayRow { Date = date };

            foreach (var t in transactions.Where(t => t.Date.Date == date.Date))
            {
                if (TransactionClassification.IsInternalMovement(t))
                {
                    continue;
                }

                var matchingRule = rulesList.FirstOrDefault(r =>
                    t.Description.Contains(r.ContainsText, StringComparison.OrdinalIgnoreCase));
                var matchingBill = allBillsForMatching.FirstOrDefault(b =>
                    (!string.IsNullOrWhiteSpace(b.PaymentMatchText) &&
                     t.Description.Contains(b.PaymentMatchText, StringComparison.OrdinalIgnoreCase)) ||
                    (Math.Abs(t.AmountCents) == b.AmountCents && b.AmountCents > 0));

                dayRow.Transactions.Add(new DailyTrackerTransactionRow
                {
                    Id = t.Id,
                    Description = t.Description,
                    ResolvedDisplayName = matchingRule?.DisplayName ?? string.Empty,
                    Amount = t.AmountDollars,
                    AccountName = t.Account?.Name ?? "",
                    CategoryName = GetDisplayCategoryName(t),
                    IsUnnecessary = t.IsUnnecessary,
                    IsBillPayment = matchingBill is not null,
                    BillName = matchingBill?.Name ?? string.Empty
                });
            }

            foreach (var occ in billOccurrences.Where(o => o.Date.Date == date.Date))
            {
                dayRow.Bills.Add(new BillCalendarBillRow
                {
                    BillId = occ.Bill.Id,
                    DueDate = occ.Date,
                    Name = occ.Bill.Name,
                    Amount = occ.Bill.AmountDollars,
                    IsPaid = paidStatuses.TryGetValue((occ.Bill.Id, occ.Date.Date), out var paid) && paid
                });
            }

            DailyTrackerDays.Add(dayRow);
        }

        var todayRow = DailyTrackerDays.FirstOrDefault(d => d.Date.Date == today);
        TodayUnnecessarySpending = todayRow?.UnnecessaryTotal ?? 0;
        TodayNecessarySpending = todayRow?.NecessaryTotal ?? 0;

        string[] dayAbbrevs = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (var i = 0; i < DailyTrackerDays.Count && i < 7; i++)
        {
            var dr = DailyTrackerDays[i];
            var isFuture = dr.Date.Date > today;
            var isToday = dr.Date.Date == today;
            WeekCalendarDays.Add(new WeekCalendarDay
            {
                DayAbbrev = dayAbbrevs[i],
                SpendingDisplay = isFuture ? "" : dr.HasSpending ? dr.SpendingTotal.ToString("C") : "$0",
                Grade = isFuture ? "—" : dr.HasSpending ? dr.DayGrade : "·",
                GradeColorHex = isFuture ? "#334155" : dr.HasSpending ? dr.DayScoreColorHex : "#475569",
                BackgroundHex = isToday ? "#0F172A" : "#0B1120",
                BorderHex = isToday ? "#6EE7B7" : "#243244",
                IsToday = isToday,
                IsFuture = isFuture
            });
        }

        var spendingDays = DailyTrackerDays.Where(d => d.HasSpending && d.Date.Date <= today).ToList();
        WeeklyScore = spendingDays.Count > 0 ? (int)spendingDays.Average(d => d.DayScore) : 100;
        PeriodTotalSpending = DailyTrackerDays.Sum(d => d.SpendingTotal);
        PeriodUnnecessarySpending = DailyTrackerDays.Sum(d => d.UnnecessaryTotal);
        var categoryTotals = DailyTrackerDays
            .SelectMany(d => d.Transactions)
            .GroupBy(t => string.IsNullOrWhiteSpace(t.CategoryName) ? "Uncategorised" : t.CategoryName)
            .Select(g => new DailyCategoryRow
            {
                Name = g.Key,
                Total = g.Sum(t => Math.Abs(t.Amount)),
                WeeklyLimit = categoryLimitMap.TryGetValue(g.Key, out var lim) ? lim : 0
            })
            .OrderByDescending(c => c.Total)
            .Take(6)
            .ToList();
        DailyTopCategories.Clear();
        foreach (var c in categoryTotals) DailyTopCategories.Add(c);

        // Last-week comparison
        var lastMonday = monday.AddDays(-7);
        var lastSundayEnd = monday.AddSeconds(-1);
        var lastWeekTx = db.Transactions
            .Include(t => t.Account)
            .Where(t => t.Date >= lastMonday && t.Date <= lastSundayEnd && t.AmountCents < 0)
            .AsNoTracking()
            .ToList();
        LastWeekTotalSpending = lastWeekTx
            .Where(t => !TransactionClassification.IsInternalMovement(t))
            .Sum(t => Math.Abs(t.AmountDollars));
        WeekOnWeekChange = PeriodTotalSpending - LastWeekTotalSpending;

        // Safe to spend today
        if (SafeToSpendAmount > 0)
        {
            var daysLeft = Math.Max((int)(monday.AddDays(7) - today).TotalDays, 1);
            SafeToSpendToday = Math.Max((SafeToSpendAmount - PeriodTotalSpending) / daysLeft, 0);
        }
        else
        {
            SafeToSpendToday = 0;
        }

        ComputeUnnecessaryStreak(transactions);
        LoadMonthCalendar();
    }

    private void LoadMonthCalendar()
    {
        MonthCalendarCells.Clear();
        var today = DateTime.Today;
        const int days = 35;
        var startDate = today.AddDays(-(days - 1));

        using var db = new FinoraDbContext();
        var txns = db.Transactions
            .Where(t => t.Date >= startDate && t.AmountCents < 0)
            .AsNoTracking()
            .ToList()
            .Where(t => !TransactionClassification.IsInternalMovement(t))
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Pad to Monday-aligned start
        var firstDow = (int)startDate.DayOfWeek; // 0=Sun,1=Mon,...
        var padCount = (firstDow - 1 + 7) % 7;
        for (var i = 0; i < padCount; i++)
            MonthCalendarCells.Add(new MonthCalendarCell { IsPadding = true, BackgroundHex = "Transparent", BorderHex = "Transparent" });

        for (var i = 0; i < days; i++)
        {
            var date = startDate.AddDays(i);
            var dayTxns = txns.GetValueOrDefault(date.Date);
            var spending = dayTxns?.Sum(t => Math.Abs(t.AmountDollars)) ?? 0m;
            var unnecessary = dayTxns?.Where(t => t.IsUnnecessary).Sum(t => Math.Abs(t.AmountDollars)) ?? 0m;
            var necessary = spending - unnecessary;
            var score = spending == 0 ? 100 : (int)(necessary / spending * 100);
            var grade = spending == 0 ? "—" : score switch
            {
                100 => "A+", >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 50 => "D", _ => "F"
            };
            var gradeColor = spending == 0 ? "#334155" : score switch
            {
                100 => "#34D399", >= 80 => "#6EE7B7", >= 60 => "#FBBF24", >= 40 => "#F97316", _ => "#F87171"
            };
            var bgColor = spending == 0 ? "#0D1117" : score switch
            {
                100 => "#0D2E22", >= 80 => "#0D2420", >= 60 => "#2D200A", >= 40 => "#2D1810", _ => "#2D1214"
            };
            var isToday = date.Date == today;

            MonthCalendarCells.Add(new MonthCalendarCell
            {
                Day = date.Day,
                Grade = grade,
                GradeColorHex = gradeColor,
                BackgroundHex = bgColor,
                BorderHex = isToday ? "#6EE7B7" : "#1C2433",
                SpendingText = spending > 0 ? spending.ToString("C") : "",
                HasSpending = spending > 0
            });
        }
    }

    public void ToggleTransactionUnnecessary(int transactionId, bool isUnnecessary)
    {
        using var db = new FinoraDbContext();
        var transaction = db.Transactions.FirstOrDefault(t => t.Id == transactionId);
        if (transaction is null)
        {
            return;
        }

        transaction.IsUnnecessary = isUnnecessary;
        db.SaveChanges();

        foreach (var day in DailyTrackerDays)
        {
            var row = day.Transactions.FirstOrDefault(t => t.Id == transactionId);
            if (row is not null)
            {
                row.IsUnnecessary = isUnnecessary;
                break;
            }
        }

        var todayRow = DailyTrackerDays.LastOrDefault();
        TodayUnnecessarySpending = todayRow?.UnnecessaryTotal ?? 0;
        TodayNecessarySpending = todayRow?.NecessaryTotal ?? 0;
        var spendingDays2 = DailyTrackerDays.Where(d => d.HasSpending).ToList();
        WeeklyScore = spendingDays2.Count > 0 ? (int)spendingDays2.Average(d => d.DayScore) : 100;
        PeriodTotalSpending = DailyTrackerDays.Sum(d => d.SpendingTotal);
        PeriodUnnecessarySpending = DailyTrackerDays.Sum(d => d.UnnecessaryTotal);
        using var db2 = new FinoraDbContext();
        var limitMap2 = LoadCategoryLimitsFromDb(db2)
            .ToDictionary(l => l.Category, l => l.WeeklyLimit, StringComparer.OrdinalIgnoreCase);
        var catTotals2 = DailyTrackerDays
            .SelectMany(d => d.Transactions)
            .GroupBy(t => string.IsNullOrWhiteSpace(t.CategoryName) ? "Uncategorised" : t.CategoryName)
            .Select(g => new DailyCategoryRow
            {
                Name = g.Key,
                Total = g.Sum(t => Math.Abs(t.Amount)),
                WeeklyLimit = limitMap2.TryGetValue(g.Key, out var lim2) ? lim2 : 0
            })
            .OrderByDescending(c => c.Total)
            .Take(6)
            .ToList();
        DailyTopCategories.Clear();
        foreach (var c in catTotals2) DailyTopCategories.Add(c);
        var recentTx = db2.Transactions
            .Where(t => t.Date >= DateTime.Today.AddDays(-60))
            .AsNoTracking()
            .ToList();
        ComputeUnnecessaryStreak(recentTx);
    }

    private void ComputeUnnecessaryStreak(IEnumerable<Transaction> transactions)
    {
        var txByDay = transactions
            .Where(t => t.AmountCents < 0 && !TransactionClassification.IsInternalMovement(t))
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var today = DateTime.Today;
        var streak = 0;

        for (var i = 1; i <= 60; i++)
        {
            var date = today.AddDays(-i);
            if (!txByDay.TryGetValue(date, out var dayTx) || dayTx.Count == 0)
            {
                streak++;
                continue;
            }

            if (dayTx.Any(t => t.IsUnnecessary))
            {
                break;
            }

            streak++;
        }

        UnnecessaryStreakDays = streak;
        UnnecessaryStreakDisplay = streak == 0
            ? "Mark a transaction as unnecessary to start tracking."
            : streak == 1
                ? "1 day without unnecessary spending"
                : $"{streak} days without unnecessary spending";
    }

    private void LoadBillPaymentHistory(FinoraDbContext db)
    {
        var history = db.BillOccurrenceStatuses
            .Include(s => s.Bill)
            .Where(s => s.IsPaid || s.IsSkipped)
            .OrderByDescending(s => s.DueDate)
            .Take(30)
            .ToList();
        var transactionIds = history
            .Where(s => s.MatchedTransactionId is not null)
            .Select(s => s.MatchedTransactionId!.Value)
            .Distinct()
            .ToList();
        var transactions = db.Transactions
            .Where(t => transactionIds.Contains(t.Id))
            .ToDictionary(t => t.Id);

        foreach (var status in history)
        {
            var matched = status.MatchedTransactionId is { } transactionId && transactions.TryGetValue(transactionId, out var transaction)
                ? $"{transaction.Date:dd/MM/yyyy} {transaction.Description} {transaction.AmountDollars:C}"
                : "";
            BillPaymentHistory.Add(new BillPaymentHistoryRow
            {
                BillId = status.BillId,
                BillName = status.Bill?.Name ?? "Bill",
                DueDate = status.DueDate,
                Status = status.IsSkipped ? "Skipped" : "Paid",
                PaidOnDisplay = status.PaidOn is null ? "" : status.PaidOn.Value.ToString("dd/MM/yyyy"),
                MatchedTransaction = matched,
                MatchNote = status.MatchNote
            });
        }
    }

    public void RefreshAfterBillPaymentChange()
    {
        LoadBills();
        LoadAccounts();
        LoadDebts();
        LoadDebtPaymentAudit();
        LoadDebtStrategies();
        LoadCashForecast();
        LoadDangerAlerts();
    }

    public void RefreshAfterDebtChange()
    {
        LoadDebts();
        LoadDebtPaymentAudit();
        LoadDebtStrategies();
    }

    public void PreviousBillCalendarMonth()
    {
        if (SummaryPeriod == "Weekly")
        {
            WeeklyPageOffset--;
            LoadTransactions();
        }
        else if (SummaryPeriod == "Monthly")
        {
            MonthlyPageOffset++;
            LoadTransactions();
        }
    }

    public void NextBillCalendarMonth()
    {
        if (SummaryPeriod == "Weekly")
        {
            WeeklyPageOffset++;
            LoadTransactions();
        }
        else if (SummaryPeriod == "Monthly")
        {
            MonthlyPageOffset--;
            LoadTransactions();
        }
    }

    public void CurrentBillCalendarMonth()
    {
        if (SummaryPeriod == "Monthly")
        {
            MonthlyPageOffset = 0;
            LoadTransactions();
        }
        else if (SummaryPeriod == "Weekly")
        {
            WeeklyPageOffset = 0;
            LoadTransactions();
        }
        else
        {
            LoadBillCalendar();
        }
    }

    public void LoadBillCalendar()
    {
        BillCalendarDays.Clear();

        var (periodStart, periodEnd) = GetSummaryPeriodRange();
        var isWeekly = SummaryPeriod == "Weekly";
        var firstVisibleDay = isWeekly ? periodStart : GetStartOfWeek(periodStart);
        var lastVisibleDay = isWeekly ? periodEnd : GetEndOfWeek(periodEnd);
        var visibleMonth = new DateTime(periodStart.Year, periodStart.Month, 1);

        using var db = new FinoraDbContext();
        var bills = db.Bills
            .OrderBy(b => b.Name)
            .ToList();
        var billOccurrences = GetVisibleBillOccurrences(db, bills, firstVisibleDay, lastVisibleDay)
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Bill.Name)
            .ToList();
        var billIds = billOccurrences.Select(o => o.Bill.Id).Distinct().ToList();
        var paidStatuses = db.BillOccurrenceStatuses
            .Where(s => billIds.Contains(s.BillId) && s.DueDate >= firstVisibleDay.Date && s.DueDate <= lastVisibleDay.Date)
            .ToList()
            .ToDictionary(s => (s.BillId, s.DueDate.Date), s => s.IsPaid);

        var visibleDayCount = (lastVisibleDay.Date - firstVisibleDay.Date).Days + 1;
        for (var i = 0; i < visibleDayCount; i++)
        {
            var date = firstVisibleDay.AddDays(i);
            var day = new BillCalendarDayRow
            {
                Date = date,
                IsCurrentMonth = isWeekly || date.Month == visibleMonth.Month,
                IsInSelectedPeriod = date.Date >= periodStart.Date && date.Date <= periodEnd.Date
            };

            foreach (var occurrence in billOccurrences.Where(o => o.Date == date.Date))
            {
                var bill = occurrence.Bill;
                day.Bills.Add(new BillCalendarBillRow
                {
                    BillId = bill.Id,
                    DueDate = occurrence.Date,
                    Name = bill.Name,
                    Amount = bill.AmountDollars,
                    IsPaid = paidStatuses.TryGetValue((bill.Id, occurrence.Date.Date), out var isPaid) && isPaid
                });
            }

            BillCalendarDays.Add(day);
        }

        OnPropertyChanged(nameof(BillCalendarMonthTitle));
        OnPropertyChanged(nameof(BillCalendarTitle));
        OnPropertyChanged(nameof(BillCalendarSubtitle));
        OnPropertyChanged(nameof(BillCalendarVisibleTotal));
        OnPropertyChanged(nameof(BillCalendarBillCount));
        OnPropertyChanged(nameof(BillCalendarPaidCount));
        OnPropertyChanged(nameof(BillCalendarUnpaidCount));
        OnPropertyChanged(nameof(BillCalendarModeSummary));
        OnPropertyChanged(nameof(BillCalendarDayHeaders));
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        return date.Date.AddDays(-(int)date.DayOfWeek);
    }

    private static DateTime GetEndOfWeek(DateTime date)
    {
        return date.Date.AddDays(6 - (int)date.DayOfWeek);
    }

    private static IReadOnlyList<(DateTime Date, Bill Bill)> ExpandBillOccurrences(IEnumerable<Bill> bills, DateTime start, DateTime end)
    {
        var occurrences = new List<(DateTime Date, Bill Bill)>();

        foreach (var bill in bills)
        {
            var dueDate = bill.DueDate.Date;
            while (dueDate < start.Date)
            {
                dueDate = GetNextBillDueDate(dueDate, bill.Frequency);
            }

            while (dueDate <= end.Date)
            {
                occurrences.Add((dueDate, bill));
                dueDate = GetNextBillDueDate(dueDate, bill.Frequency);
            }
        }

        return occurrences;
    }

    private static IReadOnlyList<(DateTime Date, Bill Bill)> GetVisibleBillOccurrences(FinoraDbContext db, IEnumerable<Bill> bills, DateTime start, DateTime end)
    {
        var occurrences = ExpandBillOccurrences(bills, start, end);
        var skipped = db.BillOccurrenceStatuses
            .Where(s => s.IsSkipped && s.DueDate >= start.Date && s.DueDate <= end.Date)
            .ToList()
            .Select(s => (s.BillId, s.DueDate.Date))
            .ToHashSet();

        return occurrences
            .Where(o => !skipped.Contains((o.Bill.Id, o.Date.Date)))
            .ToList();
    }

    private static bool IsBillOccurrencePaid(FinoraDbContext db, int billId, DateTime dueDate)
    {
        return db.BillOccurrenceStatuses
            .Where(s => s.BillId == billId && s.DueDate == dueDate.Date)
            .Select(s => s.IsPaid)
            .FirstOrDefault();
    }

    internal static DateTime GetNextBillDueDate(DateTime dueDate, BillFrequency frequency)
    {
        return frequency switch
        {
            BillFrequency.Weekly      => dueDate.AddDays(7),
            BillFrequency.Fortnightly => dueDate.AddDays(14),
            BillFrequency.Monthly     => dueDate.AddMonths(1),
            BillFrequency.Quarterly   => dueDate.AddMonths(3),
            BillFrequency.Yearly      => dueDate.AddYears(1),
            _                         => dueDate.AddMonths(1)
        };
    }

    private static DateTime GetPreviousBillDueDate(DateTime dueDate, BillFrequency frequency)
    {
        return frequency switch
        {
            BillFrequency.Weekly      => dueDate.AddDays(-7),
            BillFrequency.Fortnightly => dueDate.AddDays(-14),
            BillFrequency.Monthly     => dueDate.AddMonths(-1),
            BillFrequency.Quarterly   => dueDate.AddMonths(-3),
            BillFrequency.Yearly      => dueDate.AddYears(-1),
            _                         => dueDate.AddMonths(-1)
        };
    }

    /// <summary>
    /// How much of this bill's amount should have been saved by now, assuming an even
    /// weekly contribution across its current billing cycle (from the previous due date
    /// to the next/current due date). Used to judge whether a sinking-fund account is
    /// actually behind pace, rather than just short of the full bill amount.
    /// </summary>
    private static decimal GetExpectedBillContribution(Bill bill, DateTime today)
    {
        var nextDue = bill.DueDate.Date;
        while (nextDue < today.Date)
        {
            nextDue = GetNextBillDueDate(nextDue, bill.Frequency);
        }

        var cycleStart = GetPreviousBillDueDate(nextDue, bill.Frequency);
        var totalDays = (nextDue - cycleStart).TotalDays;
        if (totalDays <= 0)
        {
            return bill.AmountDollars;
        }

        var elapsedDays = Math.Clamp((today.Date - cycleStart).TotalDays, 0, totalDays);
        return Math.Round(bill.AmountDollars * (decimal)(elapsedDays / totalDays), 2);
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

    public void LoadCategories()
    {
        Categories.Clear();

        using var db = new FinoraDbContext();
        foreach (var category in db.Categories.OrderBy(c => c.Type).ThenBy(c => c.Name).ToList())
        {
            Categories.Add(new CategoryRow
            {
                Id = category.Id,
                Name = category.Name,
                Type = category.Type.ToString()
            });
        }
    }

    public void LoadBudget(bool loadInsights = true)
    {
        using var db = new FinoraDbContext();
        var budget = db.WeeklyBudgets.FirstOrDefault();

        WeeklyIncome = budget?.IncomeDollars ?? 0;
        BudgetBills = budget?.BillsDollars ?? 0;
        BudgetEssentials = budget?.EssentialsDollars ?? 0;
        BudgetUnplanned = budget?.UnplannedDollars ?? 0;

        // Derive savings from goals; clear if no goals have a weekly contribution
        var goalSavings = Math.Round(
            (db.SavingsGoals.Sum(g => (int?)g.WeeklyContributionCents) ?? 0) / 100m, 2);
        BudgetSavings = goalSavings;
        if (budget is not null && budget.SavingsDollars != goalSavings)
        {
            budget.SavingsDollars = goalSavings;
            db.SaveChanges();
        }

        LoadBudgetBreakdown();
        LoadBillFundingPlan();
        // BudgetBreakdownRows is now populated — reload tiles so per-bill detail tiles are filled in.
        LoadSavedBudgetTiles();
        if (loadInsights)
        {
            LoadInsights();
        }
        OnPropertyChanged(nameof(BudgetLeftover));
    }

    private static WeeklyBudget? GetWeeklyBudget()
    {
        using var db = new FinoraDbContext();
        return db.WeeklyBudgets.FirstOrDefault();
    }

    public void SaveBudget(decimal weeklyIncome, decimal bills, decimal essentials, decimal savings, decimal unplanned)
    {
        using var db = new FinoraDbContext();
        var budgets = db.WeeklyBudgets.OrderBy(b => b.Id).ToList();
        var budget = budgets.FirstOrDefault();
        if (budget is null)
        {
            budget = new WeeklyBudget();
            db.WeeklyBudgets.Add(budget);
        }
        else if (budgets.Count > 1)
        {
            db.WeeklyBudgets.RemoveRange(budgets.Skip(1));
        }

        budget.IncomeDollars = weeklyIncome;
        budget.BillsDollars = bills;
        budget.EssentialsDollars = essentials;
        budget.SavingsDollars = savings;
        budget.UnplannedDollars = unplanned;
        db.SaveChanges();

        SaveBudgetSnapshot(db, weeklyIncome, bills, essentials, savings, unplanned);
        LoadBudget();
    }

    public BudgetSuggestion BuildSuggestedBudget()
    {
        using var db = new FinoraDbContext();
        var includedKeys = LoadBudgetIncludedItemKeys(db);
        var excludedKeys = LoadBudgetExcludedItemKeys(db);
        var transferTargets = LoadBudgetTransferTargets(db);
        var today = DateTime.Today;
        var historyStart = today.AddDays(-90);
        var recentTransactions = db.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Date >= historyStart && t.Date <= today)
            .ToList();

        var oldestTransactionDate = recentTransactions.Count == 0
            ? today
            : recentTransactions.Min(t => t.Date.Date);
        var observedWeeks = Math.Max((decimal)(today - oldestTransactionDate).TotalDays / 7m, 1m);

        var weeklyIncome = RoundDollars(recentTransactions
            .Where(IsIncomeTransaction)
            .Sum(t => t.AmountDollars) / observedWeeks);
        if (weeklyIncome <= 0)
        {
            weeklyIncome = WeeklyIncome;
        }

        var breakdown = new List<BudgetBreakdownRow>();
        var bills = db.Bills
            .AsNoTracking()
            .Include(b => b.Account)
            .OrderBy(b => b.DueDate)
            .ThenBy(b => b.Name)
            .ToList();
        var weeklyBills = RoundDollars(bills.Sum(GetWeeklyBillAmount));
        breakdown.AddRange(bills.Select(b => new BudgetBreakdownRow
        {
                Bucket = "Bills",
                Name = b.Name,
                Amount = RoundDollars(GetWeeklyBillAmount(b)),
                Detail = $"{b.Frequency} bill, {b.AmountDollars:C} due {b.DueDate:dd/MM/yyyy}",
                TransferTo = b.Account?.Name ?? "Bills account",
                ExclusionKey = BuildBudgetItemKey("Bills", b.Name),
                BillId = b.Id,
                IsDefaultIncluded = true,
                IsIncluded = !excludedKeys.Contains(BuildBudgetItemKey("Bills", b.Name))
            }));

        if (weeklyBills <= 0)
        {
            var billCategoryRows = BuildCategoryBreakdown(
                recentTransactions,
                observedWeeks,
                IsBillCategory,
                "Bills",
                "Recent bill-category spending");

            weeklyBills = RoundDollars(billCategoryRows.Sum(r => r.Amount));
            ApplyBudgetInclusions(billCategoryRows, includedKeys);
            breakdown.AddRange(billCategoryRows);
        }

        var billTargetTopUps = BuildBillAccountTargetTopUpRows(db, breakdown, excludedKeys);
        breakdown.AddRange(billTargetTopUps);
        weeklyBills = RoundDollars(breakdown.Where(r => r.Bucket == "Bills" && r.IsIncluded).Sum(r => r.Amount));

        // Accounts that already have bill rows in the budget — their target contribution is
        // handled by the top-up rows above, so saverRows must not double-count them.
        var billCoveredAccountNames = breakdown
            .Where(r => r.Bucket == "Bills" && r.IsIncluded && !string.IsNullOrWhiteSpace(r.TransferTo))
            .Select(r => r.TransferTo.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var essentialRows = BuildCategoryBreakdown(
            recentTransactions,
            observedWeeks,
            IsEssentialCategory,
            "Essentials",
            "Recent essential spending");
        ApplyBudgetInclusions(essentialRows, includedKeys);
        var weeklyEssentials = RoundDollars(essentialRows.Where(r => r.IsIncluded).Sum(r => r.Amount));
        breakdown.AddRange(essentialRows);

        var unplannedRows = BuildCategoryBreakdown(
            recentTransactions,
            observedWeeks,
            name => !IsEssentialCategory(name) && !IsBillCategory(name),
            "Unplanned",
            "Recent flexible spending");
        ApplyBudgetInclusions(unplannedRows, includedKeys);
        var weeklyUnplanned = RoundDollars(unplannedRows.Where(r => r.IsIncluded).Sum(r => r.Amount));
        breakdown.AddRange(unplannedRows);

        var hasSavedPlannerSavings = BudgetSavings > 0;
        var customRows = LoadCustomBudgetItems(db)
            .Where(item => !hasSavedPlannerSavings || !IsPlannerSavingsCustomItem(item))
            .Select(item => new BudgetBreakdownRow
            {
                Bucket = item.Bucket,
                Name = item.Name,
                Amount = item.Amount,
                Detail = IsTemplateBudgetItem(item)
                    ? "Budget template suggestion. Untick this tile to remove it from the generated budget."
                    : "Manually added budget item",
                TransferTo = item.TransferTo,
                ExclusionKey = BuildBudgetItemKey(item.Bucket, item.Name)
            })
            .Where(r => r.Amount > 0)
            .ToList();
        ApplyBudgetInclusions(customRows, includedKeys, excludedKeys);
        weeklyUnplanned = RoundDollars(weeklyUnplanned + customRows.Where(r => r.IsIncluded && r.Bucket == "Unplanned").Sum(r => r.Amount));
        breakdown.AddRange(customRows);

        var savingsRows = db.SavingsGoals
            .AsNoTracking()
            .OrderBy(g => g.TargetDate ?? DateTime.MaxValue)
            .ThenBy(g => g.Name)
            .ToList()
            .Select(g => new BudgetBreakdownRow
            {
                Bucket = "Savings",
                Name = BuildSavingsGoalBudgetName(g.Name),
                Amount = RoundDollars(g.WeeklyContributionDollars),
                Detail = g.TargetDate is null
                    ? $"{g.CurrentDollars:C} saved of {g.TargetDollars:C}"
                    : $"{g.CurrentDollars:C} saved of {g.TargetDollars:C} by {g.TargetDate:dd/MM/yyyy}",
                TransferTo = g.Name,
                SavingsGoalId = g.Id,
                ExclusionKey = BuildBudgetItemKey("Savings", BuildSavingsGoalBudgetName(g.Name))
            })
            .Where(r => r.Amount > 0)
            .ToList();

        var saverRows = db.Accounts
            .AsNoTracking()
            .Include(a => a.Transactions)
            .Where(a => a.TargetCents != null && (a.Type == AccountType.Savings || a.Type == AccountType.Bills))
            .OrderBy(a => a.TargetDate ?? DateTime.MaxValue)
            .ThenBy(a => a.Name)
            .ToList()
            .Where(a => !billCoveredAccountNames.Contains(a.Name.Trim()))
            .Select(a =>
            {
                var balance = a.Transactions.Sum(t => t.AmountDollars);
                var weeklyContribution = GetWeeklyAccountTargetContribution(a, balance, NextPayDate);
                return new BudgetBreakdownRow
                {
                    Bucket = a.Name.Trim(),
                    Name = "Target",
                    Amount = weeklyContribution,
                    Detail = a.TargetDate is null
                        ? $"{balance:C} saved of {a.TargetDollars:C}"
                        : $"{balance:C} saved of {a.TargetDollars:C} by {a.TargetDate:dd/MM/yyyy}",
                    TransferTo = a.Name,
                    ExclusionKey = BuildBudgetItemKey("Savings", BuildAccountBudgetName(a.Name)),
                    AccountId = a.Id
                };
            })
            .Where(r => r.Amount > 0)
            .ToList();

        weeklyBills = RoundDollars(weeklyBills + customRows.Where(r => r.IsIncluded && r.Bucket == "Bills").Sum(r => r.Amount));
        weeklyEssentials = RoundDollars(weeklyEssentials + customRows.Where(r => r.IsIncluded && r.Bucket == "Essentials").Sum(r => r.Amount));
        // saverRows are account targets (e.g. "Licence") — counted in weeklyBills so they appear
        // in the Allocated total without inflating the Savings field.
        ApplyBudgetInclusions(saverRows, includedKeys);
        weeklyBills = RoundDollars(weeklyBills + saverRows.Where(r => r.IsIncluded).Sum(r => r.Amount));
        breakdown.AddRange(saverRows);

        // Only use the aggregate "Savings allocation" row when there are no individual goal rows.
        // If goals exist, show them directly so added goals are immediately visible.
        var savedSavingsRow = savingsRows.Count == 0
            ? BuildSavedSavingsBudgetRow(BudgetSavings, excludedKeys)
            : null;
        List<BudgetBreakdownRow> visibleSavingsRows;
        decimal weeklySavings;
        if (savedSavingsRow is not null)
        {
            visibleSavingsRows = new List<BudgetBreakdownRow> { savedSavingsRow };
            weeklySavings = savedSavingsRow.IsIncluded ? savedSavingsRow.Amount : 0;
        }
        else
        {
            ApplyBudgetInclusions(savingsRows, includedKeys);
            visibleSavingsRows = savingsRows.ToList();
            weeklySavings = RoundDollars(visibleSavingsRows.Where(r => r.IsIncluded).Sum(r => r.Amount));
        }

        breakdown.AddRange(visibleSavingsRows);
        weeklySavings = RoundDollars(weeklySavings + customRows.Where(r => r.IsIncluded && r.Bucket == "Savings").Sum(r => r.Amount));

        ApplyBudgetTransferTargets(breakdown, transferTargets);
        return FitBudgetToIncome(weeklyIncome, weeklyBills, weeklyEssentials, weeklySavings, weeklyUnplanned, breakdown);
    }

    private static BudgetBreakdownRow? BuildSavedSavingsBudgetRow(decimal savedAmount, HashSet<string> excludedKeys)
    {
        savedAmount = RoundDollars(savedAmount);
        if (savedAmount <= 0)
        {
            return null;
        }

        const string bucket = "Savings";
        const string name = SuggestedSavingsBudgetName;
        var key = BuildBudgetItemKey(bucket, name);
        return new BudgetBreakdownRow
        {
            Bucket = bucket,
            Name = name,
            Amount = savedAmount,
            Detail = "Saved savings allocation from the budget planner. Untick this tile to remove it from the generated budget.",
            TransferTo = string.Empty,
            ExclusionKey = key,
            IsDefaultIncluded = true,
            IsIncluded = !excludedKeys.Contains(NormalizeBudgetItemKey(key))
        };
    }

    private IReadOnlyList<BudgetBreakdownRow> BuildBillAccountTargetTopUpRows(
        FinoraDbContext db,
        IEnumerable<BudgetBreakdownRow> breakdown,
        HashSet<string> excludedKeys)
    {
        var includedBillsByAccount = breakdown
            .Where(r => r.Bucket == "Bills" && r.IsIncluded)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.TransferTo) ? r.Bucket : r.TransferTo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => RoundDollars(g.Sum(r => r.Amount)), StringComparer.OrdinalIgnoreCase);

        return db.Accounts
            .AsNoTracking()
            .Include(a => a.Transactions)
            .Where(a => a.TargetCents != null)
            .OrderBy(a => a.Name)
            .ToList()
            .Where(a => includedBillsByAccount.ContainsKey(a.Name.Trim()))
            .Select(account =>
            {
                var accountName = account.Name.Trim();
                var balance = account.Transactions.Sum(t => t.AmountDollars);
                var targetWeekly = GetWeeklyAccountTargetContribution(account, balance, NextPayDate);
                var billWeekly = includedBillsByAccount.GetValueOrDefault(accountName, 0);
                var topUp = RoundDollars(targetWeekly - billWeekly);
                var key = BuildBudgetItemKey("Bills", $"{accountName} target top-up");

                var shortfall = account.TargetDollars.HasValue ? Math.Max(account.TargetDollars.Value - balance, 0m) : 0m;
                var detail = account.TargetDate is null
                    ? $"{balance:C} saved — {shortfall:C} short of {account.TargetDollars:C} target. Extra weekly contribution to catch up."
                    : $"{balance:C} saved — {shortfall:C} short of {account.TargetDollars:C} needed by {account.TargetDate:dd/MM/yyyy}. Extra weekly to catch up.";

                return new BudgetBreakdownRow
                {
                    Bucket = "Bills",
                    Name = "Catch-up",
                    Amount = topUp,
                    Detail = detail,
                    TransferTo = accountName,
                    ExclusionKey = key,
                    IsDefaultIncluded = true,
                    IsIncluded = !excludedKeys.Contains(NormalizeBudgetItemKey(key))
                };
            })
            .Where(r => r.Amount > 0)
            .ToList();
    }

    public void SaveSuggestedBudget()
    {
        // When the user has already set income, preserve their manual Essentials/Savings/Unplanned
        // values. Only update Bills from the currently checked breakdown items so that toggling
        // tiles or adding bills never clobbers manually entered budget figures.
        if (WeeklyIncome > 0)
        {
            var bills = BudgetPlannerWeeklyBills > 0 ? BudgetPlannerWeeklyBills : BudgetBills;

            SaveBudget(WeeklyIncome, bills, BudgetEssentials, BudgetSavings, BudgetUnplanned);
        }
        else
        {
            var suggestion = BuildSuggestedBudget();
            SaveBudget(suggestion.WeeklyIncome, suggestion.Bills, suggestion.Essentials, suggestion.Savings, suggestion.Unplanned);
        }
    }

    public void SyncBillsBudget()
    {
        SaveBudget(WeeklyIncome, BudgetPlannerWeeklyBills, BudgetEssentials, BudgetSavings, BudgetUnplanned);
    }

    public void PreviewBudgetTemplate(string template)
    {
        var income = WeeklyIncome;
        if (income <= 0)
        {
            return;
        }

        var bills = BudgetPlannerWeeklyBills > 0 ? BudgetPlannerWeeklyBills : BudgetBills;
        var remaining = Math.Max(income - bills, 0);
        var (essentialsShare, savingsShare, unplannedShare) = template switch
        {
            "Lean" => (0.62m, 0.28m, 0.10m),
            "Debt payoff" => (0.50m, 0.40m, 0.10m),
            _ => (0.55m, 0.25m, 0.20m)
        };

        BudgetTemplateName = template;
        BudgetTemplateIncome = income;
        BudgetTemplateBills = RoundDollars(bills);
        BudgetTemplateEssentials = RoundDollars(remaining * essentialsShare);
        BudgetTemplateSavings = RoundDollars(remaining * savingsShare);
        BudgetTemplateUnplanned = RoundDollars(remaining * unplannedShare);
        RefreshBudgetTemplateSuggestion();
    }

    public void ApplyBudgetTemplateSuggestion()
    {
        if (!HasBudgetTemplateSuggestion)
        {
            return;
        }

        SaveTemplateBudgetItems();
        SaveBudget(BudgetTemplateIncome, BudgetTemplateBills, BudgetTemplateEssentials, BudgetTemplateSavings, BudgetTemplateUnplanned);
    }

    public void ClearBudgetTemplateSuggestion()
    {
        BudgetTemplateName = string.Empty;
        BudgetTemplateIncome = 0;
        BudgetTemplateBills = 0;
        BudgetTemplateEssentials = 0;
        BudgetTemplateSavings = 0;
        BudgetTemplateUnplanned = 0;
        RefreshBudgetTemplateSuggestion();
    }

    public void ResetSavedBudget()
    {
        ClearTemplateBudgetItems();
        ClearSavingsRecommendationDecline();
        SaveBudget(WeeklyIncome, 0, 0, 0, 0);
        ClearBudgetTemplateSuggestion();
    }

    private void SaveTemplateBudgetItems()
    {
        using var db = new FinoraDbContext();
        var items = LoadCustomBudgetItems(db)
            .Where(item => !IsTemplateBudgetItem(item))
            .ToList();

        AddTemplateBudgetItem(items, "Essentials", "Template essentials", BudgetTemplateEssentials);
        AddTemplateBudgetItem(items, "Savings", "Template savings", BudgetTemplateSavings);
        AddTemplateBudgetItem(items, "Unplanned", "Template unplanned", BudgetTemplateUnplanned);

        SaveCustomBudgetItems(db, items);
        AddTemplateBudgetItemKey(db, "Essentials", "Template essentials", BudgetTemplateEssentials);
        AddTemplateBudgetItemKey(db, "Savings", "Template savings", BudgetTemplateSavings);
        AddTemplateBudgetItemKey(db, "Unplanned", "Template unplanned", BudgetTemplateUnplanned);
        db.SaveChanges();
    }

    private static void AddTemplateBudgetItem(List<CustomBudgetItem> items, string bucket, string name, decimal amount)
    {
        amount = RoundDollars(amount);
        if (amount <= 0)
        {
            return;
        }

        items.Add(new CustomBudgetItem(bucket, name, amount, string.Empty));
    }

    private static void AddTemplateBudgetItemKey(FinoraDbContext db, string bucket, string name, decimal amount)
    {
        if (amount <= 0)
        {
            return;
        }

        AddBudgetItemKey(db, BuildBudgetItemKey(bucket, name));
    }

    private void ClearTemplateBudgetItems()
    {
        using var db = new FinoraDbContext();
        var items = LoadCustomBudgetItems(db)
            .Where(item => !IsTemplateBudgetItem(item))
            .ToList();
        SaveCustomBudgetItems(db, items);

        var templateKeys = new[]
        {
            BuildBudgetItemKey("Essentials", "Template essentials"),
            BuildBudgetItemKey("Savings", "Template savings"),
            BuildBudgetItemKey("Unplanned", "Template unplanned")
        };
        var includedKeys = LoadBudgetIncludedItemKeys(db);
        var excludedKeys = LoadBudgetExcludedItemKeys(db);
        foreach (var key in templateKeys.Select(NormalizeBudgetItemKey))
        {
            includedKeys.Remove(key);
            excludedKeys.Remove(key);
        }

        SaveBudgetIncludedItemKeys(db, includedKeys);
        SaveBudgetExcludedItemKeys(db, excludedKeys);
        db.SaveChanges();
    }

    private void RefreshBudgetTemplateSuggestion()
    {
        OnPropertyChanged(nameof(HasBudgetTemplateSuggestion));
        OnPropertyChanged(nameof(IsNormalTemplateActive));
        OnPropertyChanged(nameof(IsLeanTemplateActive));
        OnPropertyChanged(nameof(IsDebtTemplateActive));
        OnPropertyChanged(nameof(BudgetTemplateLeftover));
        OnPropertyChanged(nameof(BudgetTemplateSummary));
        OnPropertyChanged(nameof(BudgetTemplateSavingsNote));
        OnPropertyChanged(nameof(BudgetTemplateSavingsBreakdown));
    }

    private void LoadBudgetBreakdown()
    {
        var suggestion = BuildSuggestedBudget();
        BudgetBreakdownRows.Clear();
        foreach (var row in suggestion.Breakdown.Where(r => r.IsIncluded))
        {
            BudgetBreakdownRows.Add(row);
        }

        BudgetBreakdownGroups.Clear();
        foreach (var group in suggestion.Breakdown
            .Where(r => r.IsIncluded)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.TransferTo) ? r.Bucket : r.TransferTo.Trim())
            .OrderBy(g => GetBudgetGroupSortOrder(g)))
        {
            BudgetBreakdownGroups.Add(new BudgetBreakdownGroup
            {
                Bucket = group.Key,
                Total = RoundDollars(group.Sum(r => r.Amount)),
                ExcludedTotal = 0,
                Rows = new ObservableCollection<BudgetBreakdownRow>(group
                    .OrderBy(r => r.Bucket)
                    .ThenByDescending(r => r.Amount)
                    .ThenBy(r => r.Name))
            });
        }

        OnPropertyChanged(nameof(BudgetPlannerWeeklySavers));
        OnPropertyChanged(nameof(BudgetPlannerWeeklyTransfers));
        OnPropertyChanged(nameof(BudgetPlannerIncomeGap));
        OnPropertyChanged(nameof(SafeToSpendAmount));
    }

    private void LoadBillFundingPlan()
    {
        BillFundingPlanRows.Clear();

        using var db = new FinoraDbContext();
        var accountBalances = db.Accounts
            .Include(a => a.Transactions)
            .ToDictionary(a => a.Id, a => new
            {
                a.Name,
                Balance = a.Transactions.Sum(t => t.AmountDollars)
            });

        var unpaidThroughPayday = db.Bills
            .Include(b => b.Account)
            .AsEnumerable()
            .SelectMany(bill => GetVisibleBillOccurrences(db, new[] { bill }, DateTime.MinValue, NextPayDate)
                .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
                .Select(o => new { Bill = bill, o.Date, Amount = bill.AmountDollars }))
            .GroupBy(o => o.Bill.AccountId)
            .ToDictionary(g => g.Key, g => g.Select(o => (o.Date, o.Amount)).ToList());

        var rows = db.Bills
            .Include(b => b.Account)
            .AsEnumerable()
            .GroupBy(b => b.AccountId)
            .Select(group =>
            {
                var account = accountBalances.GetValueOrDefault(group.Key);
                var weeklyAmount = RoundDollars(group.Sum(GetWeeklyBillAmount));
                var unpaidOccurrences = unpaidThroughPayday.GetValueOrDefault(group.Key) ?? new List<(DateTime Date, decimal Amount)>();
                var dueBeforePayday = RoundDollars(unpaidOccurrences
                    .Where(o => o.Date.Date < NextPayDate.Date)
                    .Sum(o => o.Amount));
                var dueOnPayday = RoundDollars(unpaidOccurrences
                    .Where(o => o.Date.Date == NextPayDate.Date)
                    .Sum(o => o.Amount));
                var dueThroughPayday = dueBeforePayday + dueOnPayday;
                var currentBalance = account?.Balance ?? 0;
                return new BillFundingPlanRow
                {
                    SaverName = account?.Name ?? group.First().Account?.Name ?? "Bills saver",
                    WeeklyAmount = weeklyAmount,
                    MonthlyAmount = RoundDollars(weeklyAmount * 52m / 12m),
                    CurrentBalance = currentBalance,
                    DueBeforePayday = dueBeforePayday,
                    DueOnPayday = dueOnPayday,
                    NeededBeforePayday = Math.Max(dueBeforePayday - currentBalance, 0),
                    PaydayTopUp = Math.Max(dueThroughPayday - currentBalance, 0),
                    BillCount = group.Count(),
                    DueBeforePaydayCount = unpaidOccurrences.Count(o => o.Date.Date < NextPayDate.Date),
                    DueOnPaydayCount = unpaidOccurrences.Count(o => o.Date.Date == NextPayDate.Date),
                    NextDueDate = group.Select(b =>
                    {
                        var d = b.DueDate.Date;
                        while (d < DateTime.Today)
                            d = GetNextBillDueDate(d, b.Frequency);
                        return d;
                    }).Min()
                };
            })
            .Where(r => r.WeeklyAmount > 0)
            .OrderByDescending(r => r.WeeklyAmount)
            .ThenBy(r => r.SaverName)
            .ToList();

        foreach (var row in rows)
        {
            BillFundingPlanRows.Add(row);
        }

        OnPropertyChanged(nameof(BudgetPlannerWeeklyBills));
        OnPropertyChanged(nameof(BudgetPlannerWeeklyTransfers));
        OnPropertyChanged(nameof(BudgetPlannerIncomeGap));
        OnPropertyChanged(nameof(BillsDueBeforePayday));
        OnPropertyChanged(nameof(BillsDueOnPayday));
        OnPropertyChanged(nameof(BillsDueOnPaydayCount));
        OnPropertyChanged(nameof(PaydayTransferTotal));
        OnPropertyChanged(nameof(BillsFundingShortfall));
        OnPropertyChanged(nameof(HasBillsFundingShortfall));
        OnPropertyChanged(nameof(SafeToSpendAmount));
        OnPropertyChanged(nameof(PrePaydayBalance));
        OnPropertyChanged(nameof(PostPaydayBalance));
        OnPropertyChanged(nameof(PrePaydayNegative));
        OnPropertyChanged(nameof(PrePaydayBalanceColor));
        OnPropertyChanged(nameof(BillsDueThroughPayday));
        OnPropertyChanged(nameof(BalanceAfterAllBills));
        OnPropertyChanged(nameof(BalanceAfterAllBillsPlusIncome));
        OnPropertyChanged(nameof(BalanceAfterAllBillsNegative));
        OnPropertyChanged(nameof(BalanceAfterAllBillsColorHex));
        RefreshBudgetDerivedValues();
    }

    private void RefreshBudgetDerivedValues()
    {
        OnPropertyChanged(nameof(BudgetLeftover));
        OnPropertyChanged(nameof(BudgetTotal));
        OnPropertyChanged(nameof(BudgetUsagePercent));
        OnPropertyChanged(nameof(BudgetRemainingPercent));
        OnPropertyChanged(nameof(BudgetBillsShare));
        OnPropertyChanged(nameof(BudgetEssentialsShare));
        OnPropertyChanged(nameof(BudgetSavingsShare));
        OnPropertyChanged(nameof(BudgetUnplannedShare));
        OnPropertyChanged(nameof(BudgetLeftoverShare));
        OnPropertyChanged(nameof(BudgetSyncDifference));
        OnPropertyChanged(nameof(BudgetSyncMessage));
        OnPropertyChanged(nameof(BudgetPressureTitle));
        OnPropertyChanged(nameof(BudgetPressureMessage));
        OnPropertyChanged(nameof(ShowSavingsRecommendation));
        OnPropertyChanged(nameof(SavingsRecommendation));
        OnPropertyChanged(nameof(SuggestedSavingsRecommendationAmount));
        OnPropertyChanged(nameof(SavingsRecommendationAmount));
        OnPropertyChanged(nameof(BudgetPlannerIncomeGap));
        OnPropertyChanged(nameof(SafeToSpendAmount));
        OnPropertyChanged(nameof(CashRunwaySummary));
        OnPropertyChanged(nameof(SavedBudgetAllocationSummary));
        OnPropertyChanged(nameof(SavedBudgetSourceSummary));
        LoadSavedBudgetTiles();
        RefreshAffordability();
    }

    private void LoadSavedBudgetTiles()
    {
        SavedBudgetTiles.Clear();
        AddSavedBudgetTile("Bills", BudgetBills, "Saved weekly bill allocation", "#F8FAFC");
        AddSavedBudgetTile("Essentials", BudgetEssentials, "Saved weekly essentials allocation", "#F8FAFC");
        AddSavedBudgetTile("Targets", BudgetSavings, "Saved weekly savings and target allocation", "#6EE7B7");
        AddSavedBudgetTile("Unplanned", BudgetUnplanned, "Saved weekly flexible spending allocation", "#FBBF24");
        OnPropertyChanged(nameof(SavedBudgetTilesSummary));

        // Per-account bill tiles — one tile per bill saver account (e.g. Subscriptions, Wifi, Phone).
        // BillFundingPlanRows is empty during early property-setter calls; LoadBudget() calls
        // this method again explicitly after LoadBillFundingPlan() to fill these in correctly.
        BudgetBillDetailTiles.Clear();
        foreach (var tile in BuildBillAndTargetDetailTiles())
        {
            BudgetBillDetailTiles.Add(tile);
        }
    }

    private IReadOnlyList<SavedBudgetTileRow> BuildBillAndTargetDetailTiles()
    {
        var billRows = BudgetBreakdownRows
            .Where(r => r.IsIncluded && r.Amount > 0 && r.Bucket == "Bills")
            .GroupBy(r => string.IsNullOrWhiteSpace(r.TransferTo) ? r.Bucket : r.TransferTo.Trim())
            .Select(group =>
            {
                var billCount = group.Count(r => !string.Equals(r.Name, "Target top-up", StringComparison.OrdinalIgnoreCase));
                var hasTargetTopUp = group.Any(r => string.Equals(r.Name, "Target top-up", StringComparison.OrdinalIgnoreCase));
                var detail = billCount switch
                {
                    0 => "Target",
                    1 => hasTargetTopUp ? "1 bill + target" : "1 bill",
                    _ => hasTargetTopUp ? $"{billCount} bills + target" : $"{billCount} bills"
                };
                return new SavedBudgetTileRow
                {
                    Name = group.Key,
                    Amount = RoundDollars(group.Sum(r => r.Amount)),
                    Detail = detail,
                    ColorHex = "#CBD5E1"
                };
            })
            .ToList();

        ReconcileTileTotal(billRows, BudgetBills);

        var billAccountNames = billRows
            .Select(r => r.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetRows = BudgetBreakdownRows
            .Where(r => r.IsIncluded && r.Amount > 0 && IsAccountTargetBudgetRow(r)
                && !billAccountNames.Contains(r.Bucket))
            .Select(row => new SavedBudgetTileRow
            {
                Name = row.Bucket,
                Amount = RoundDollars(row.Amount),
                Detail = "Target",
                ColorHex = "#3FB950"
            });

        // Custom savings items (manually added, not account targets or bills)
        var targetNames = BudgetBreakdownRows
            .Where(r => IsAccountTargetBudgetRow(r))
            .Select(r => r.Bucket)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var customSavingsTiles = BudgetBreakdownRows
            .Where(r => r.IsIncluded && r.Amount > 0
                && r.Bucket == "Savings"
                && r.Name != "Target"
                && !string.Equals(r.Name, "Saved targets allocation", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Name, "Template savings", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Name, "Budget planner savings", StringComparison.OrdinalIgnoreCase))
            .Select(row => new SavedBudgetTileRow
            {
                Name = row.Name,
                Amount = RoundDollars(row.Amount),
                Detail = string.IsNullOrWhiteSpace(row.TransferTo) ? "Savings" : $"→ {row.TransferTo}",
                ColorHex = "#34D399"
            });

        return billRows
            .Concat(targetRows)
            .Concat(customSavingsTiles)
            .OrderByDescending(r => r.Amount)
            .ThenBy(r => r.Name)
            .ToList();
    }

    private static void ReconcileTileTotal(IList<SavedBudgetTileRow> rows, decimal expectedTotal)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var difference = RoundSignedDollars(expectedTotal - rows.Sum(r => r.Amount));
        if (difference == 0 || Math.Abs(difference) > 0.05m)
        {
            return;
        }

        var largest = rows.OrderByDescending(r => r.Amount).First();
        largest.Amount = RoundDollars(largest.Amount + difference);
    }

    private void AddSavedBudgetTile(string name, decimal amount, string detail, string colorHex)
    {
        if (amount <= 0)
        {
            return;
        }

        SavedBudgetTiles.Add(new SavedBudgetTileRow
        {
            Name = name,
            Amount = amount,
            Detail = detail,
            ColorHex = colorHex
        });
    }

    private void RefreshAffordability()
    {
        LoadAffordabilityData();
        // Cost / cashflow
        OnPropertyChanged(nameof(AffordabilityWeeklyCost));
        OnPropertyChanged(nameof(AffordabilityWeeklyCostDisplay));
        OnPropertyChanged(nameof(AffordabilityWeeklyCostColorHex));
        OnPropertyChanged(nameof(AffordabilityWeeklyBills));
        OnPropertyChanged(nameof(AffordabilityNetWeekly));
        OnPropertyChanged(nameof(HasAffordabilityInputs));
        // Week rows
        OnPropertyChanged(nameof(HasAffordabilityWeekRows));
        OnPropertyChanged(nameof(AffordabilityLowestBalance));
        OnPropertyChanged(nameof(AffordabilityLowestBalanceDisplay));
        OnPropertyChanged(nameof(AffordabilityEndBalanceWithDisplay));
        OnPropertyChanged(nameof(AffordabilityEndBalanceWithoutDisplay));
        OnPropertyChanged(nameof(AffordabilityEndBalanceWithColorHex));
        OnPropertyChanged(nameof(AffordabilityEndBalanceWithoutColorHex));
        OnPropertyChanged(nameof(AffordabilityWeeksSuffix));
        // Status system
        OnPropertyChanged(nameof(AffordabilityCashflowUsagePct));
        OnPropertyChanged(nameof(AffordabilityStatusIcon));
        OnPropertyChanged(nameof(AffordabilityStatusLabel));
        OnPropertyChanged(nameof(AffordabilityStatusColorHex));
        OnPropertyChanged(nameof(AffordabilityStatusDetail));
        // Bill coverage
        OnPropertyChanged(nameof(HasAffordabilityBillRows));
        OnPropertyChanged(nameof(AffordabilityAllBillsCovered));
        OnPropertyChanged(nameof(AffordabilityShortBillCount));
        OnPropertyChanged(nameof(AffordabilityTotalExtraWeeklyNeeded));
        OnPropertyChanged(nameof(AffordabilityBalanceAfterPurchase));
        OnPropertyChanged(nameof(HasAffordabilityAction));
        OnPropertyChanged(nameof(AffordabilityActionText));
        OnPropertyChanged(nameof(AffordabilityVerdictText));
        OnPropertyChanged(nameof(AffordabilityVerdictColorHex));
        OnPropertyChanged(nameof(AffordabilityWeeklyRequired));
        OnPropertyChanged(nameof(HasAffordabilityAccount));
        OnPropertyChanged(nameof(AffordabilityAccount));
        OnPropertyChanged(nameof(AffordabilityAccountBillsDue));
        OnPropertyChanged(nameof(AffordabilityAccountBudgetedWeeklyTransfer));
        OnPropertyChanged(nameof(AffordabilityAccountProjectedTopUps));
        OnPropertyChanged(nameof(AffordabilityAccountProjectedBalance));
        OnPropertyChanged(nameof(AffordabilityAccountShortfall));
        OnPropertyChanged(nameof(AffordabilityAccountExtraWeeklyNeeded));
        OnPropertyChanged(nameof(AffordabilityAccountResult));
        OnPropertyChanged(nameof(AffordabilityAccountColorHex));
        OnPropertyChanged(nameof(AffordabilityWeeklyAvailable));
        OnPropertyChanged(nameof(AffordabilityAvailable));
        OnPropertyChanged(nameof(AffordabilityDifference));
        OnPropertyChanged(nameof(AffordabilityWeeklyDifference));
        OnPropertyChanged(nameof(AffordabilitySafetyBuffer));
        OnPropertyChanged(nameof(AffordabilityMinimumWeeklyBuffer));
        OnPropertyChanged(nameof(AffordabilityStatus));
        OnPropertyChanged(nameof(AffordabilitySpareAfterPurchase));
        OnPropertyChanged(nameof(AffordabilitySpareTitle));
        OnPropertyChanged(nameof(AffordabilitySpareFormatted));
        OnPropertyChanged(nameof(AffordabilityAccountNowBalance));
        OnPropertyChanged(nameof(AffordabilityAccountNowAfterBills));
        OnPropertyChanged(nameof(AffordabilityNowColorHex));
        OnPropertyChanged(nameof(AffordabilityNowWithdrawalColorHex));
        OnPropertyChanged(nameof(AffordabilityNowBillsColorHex));
        OnPropertyChanged(nameof(AffordabilityNowWithdrawalLabel));
        OnPropertyChanged(nameof(AffordabilityNowBillsLabel));
        OnPropertyChanged(nameof(AffordabilityNowLine));
        OnPropertyChanged(nameof(AffordabilityRecommendedWeeks));
        OnPropertyChanged(nameof(AffordabilityBufferMessage));
        OnPropertyChanged(nameof(AffordabilityColorHex));
        OnPropertyChanged(nameof(AffordabilityResult));
        OnPropertyChanged(nameof(AffordabilityStat1Label));
        OnPropertyChanged(nameof(AffordabilityStat1Value));
        OnPropertyChanged(nameof(AffordabilityStat2Label));
        OnPropertyChanged(nameof(AffordabilityStat2Value));
        OnPropertyChanged(nameof(AffordabilityStat2ColorHex));
        OnPropertyChanged(nameof(AffordabilityStat3Label));
        OnPropertyChanged(nameof(AffordabilityStat3Value));
        OnPropertyChanged(nameof(AffordabilityStat3ColorHex));
    }

    private decimal GetAffordabilityAccountBillsDue()
    {
        var account = AffordabilityAccount;
        if (account is null)
        {
            return 0;
        }

        using var db = new FinoraDbContext();
        var bills = db.Bills
            .AsNoTracking()
            .Include(b => b.Account)
            .Where(b => b.AccountId == account.Id)
            .ToList();
        var endDate = DateTime.Today.AddDays(Math.Max(AffordabilityWeeks, 1) * 7);
        return RoundDollars(GetVisibleBillOccurrences(db, bills, DateTime.Today, endDate)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .Sum(o => o.Bill.AmountDollars));
    }

    private decimal GetAffordabilityAccountBudgetedWeeklyTransfer()
    {
        var account = AffordabilityAccount;
        if (account is null)
        {
            return 0;
        }

        return RoundDollars(BudgetBreakdownRows
            .Where(r => r.IsIncluded)
            .Where(r => string.Equals(r.TransferTo, account.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Bucket, account.Name, StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount));
    }

    private void LoadAffordabilityData()
    {
        AffordabilityWeekRows.Clear();
        AffordabilityBillRows.Clear();
        _affordabilityWindowBillsTotal = 0;
        _affordabilityLowestBalance = 0;
        _affordabilityLowestBalanceWeek = 0;
        _affordabilityEndBalanceWith = 0;
        _affordabilityEndBalanceWithout = 0;

        var account = AffordabilityAccount;
        if (account is null || AffordabilityAmount <= 0)
        {
            return;
        }

        using var db = new FinoraDbContext();
        var bills = db.Bills
            .AsNoTracking()
            .Include(b => b.Account)
            .Where(b => b.AccountId == account.Id)
            .ToList();

        int weeks = Math.Max(AffordabilityWeeks, 1);
        var endDate = DateTime.Today.AddDays(weeks * 7);
        var occurrences = GetVisibleBillOccurrences(db, bills, DateTime.Today, endDate)
            .Where(o => !IsBillOccurrencePaid(db, o.Bill.Id, o.Date))
            .OrderBy(o => o.Date)
            .ToList();

        decimal weeklyTopUp = GetAffordabilityAccountBudgetedWeeklyTransfer();
        decimal weeklyCost  = AffordabilityWeeklyCost;

        // ── Week-by-week projection ───────────────────────────────────────────
        decimal balance        = account.Balance;
        decimal balanceWithout = account.Balance;
        decimal lowestBalance  = decimal.MaxValue;
        int     lowestWeek     = 1;
        decimal totalBills     = 0;

        for (int week = 1; week <= weeks; week++)
        {
            var weekStart = DateTime.Today.AddDays((week - 1) * 7);
            var weekEnd   = DateTime.Today.AddDays(week * 7);

            var billsThisWeek = RoundDollars(occurrences
                .Where(o => o.Date.Date >= weekStart.Date && o.Date.Date < weekEnd.Date)
                .Sum(o => o.Bill.AmountDollars));
            totalBills += billsThisWeek;

            var startBalance   = balance;
            var endBalance     = RoundSignedDollars(balance + weeklyTopUp - weeklyCost - billsThisWeek);
            var endBalWithout  = RoundSignedDollars(balanceWithout + weeklyTopUp - billsThisWeek);

            if (endBalance < lowestBalance)
            {
                lowestBalance = endBalance;
                lowestWeek    = week;
            }

            AffordabilityWeekRows.Add(new AffordabilityWeekRow
            {
                WeekNumber     = week,
                StartBalance   = startBalance,
                TopUpAmount    = weeklyTopUp,
                BillsAmount    = billsThisWeek,
                PurchaseAmount = weeklyCost,
                EndBalance     = endBalance,
            });

            balance        = endBalance;
            balanceWithout = endBalWithout;
        }

        _affordabilityWindowBillsTotal      = totalBills;
        _affordabilityLowestBalance         = lowestBalance == decimal.MaxValue ? account.Balance : lowestBalance;
        _affordabilityLowestBalanceWeek     = lowestWeek;
        _affordabilityEndBalanceWith        = balance;
        _affordabilityEndBalanceWithout     = balanceWithout;

        // ── Per-bill coverage summary (grouped by bill name) ──────────────────
        decimal cumulativeBills = 0;
        var occurrenceResults = new List<(string Name, decimal Amount, bool Covered, decimal Balance, decimal ExtraWeekly)>();

        foreach (var occ in occurrences)
        {
            cumulativeBills += occ.Bill.AmountDollars;
            var weeksUntilDue         = Math.Max((occ.Date.Date - DateTime.Today).TotalDays / 7.0, 0.5);
            var purchaseWeeksApplied  = weeks <= 1
                ? (decimal)weeks
                : (decimal)Math.Min(weeksUntilDue, weeks);
            var projectedPurchaseCost = RoundDollars(weeklyCost * purchaseWeeksApplied);
            var projectedTopUps       = RoundDollars(weeklyTopUp * (decimal)weeksUntilDue);
            var balanceAtDueDate      = RoundSignedDollars(account.Balance + projectedTopUps - projectedPurchaseCost - cumulativeBills);
            var shortfall             = Math.Max(-balanceAtDueDate, 0m);
            var extraWeekly           = shortfall > 0 ? RoundDollars(shortfall / (decimal)weeksUntilDue) : 0m;

            occurrenceResults.Add((occ.Bill.Name, occ.Bill.AmountDollars, balanceAtDueDate >= 0, balanceAtDueDate, extraWeekly));
        }

        foreach (var group in occurrenceResults.GroupBy(r => r.Name))
        {
            var allCovered   = group.All(r => r.Covered);
            var worstBalance = group.Min(r => r.Balance);
            var maxShortfall = allCovered ? 0m : Math.Max(-worstBalance, 0m);
            var extraNeeded  = allCovered ? 0m : group.Max(r => r.ExtraWeekly);

            AffordabilityBillRows.Add(new AffordabilityBillCoverageRow
            {
                Name              = group.Key,
                WeeklyAmount      = group.First().Amount,
                OccurrenceCount   = group.Count(),
                IsCovered         = allCovered,
                MaxShortfall      = maxShortfall,
                ExtraWeeklyNeeded = allCovered ? 0m : extraNeeded,
            });
        }
    }

    public void LoadInsights()
    {
        BudgetInsightRows.Clear();
        CashFlowInsights.Clear();
        BillSaverInsights.Clear();
        SubscriptionInsights.Clear();
        SpendingInsights.Clear();
        BudgetHealthInsights.Clear();
        GoalInsights.Clear();
        CleanupInsights.Clear();

        if (BudgetPlannerWeeklyBills > 0)
        {
            var billFundingMessage = $"Set aside {BudgetPlannerWeeklyBills:C} each week across {BillFundingPlanRows.Count} saver{(BillFundingPlanRows.Count == 1 ? "" : "s")}.";
            BudgetInsightRows.Add(new BudgetInsightRow
            {
                Title = "Bills funding",
                Message = billFundingMessage,
                NavTarget = "Bills"
            });
            BillSaverInsights.Add(new BudgetInsightRow { Title = "Weekly bill transfers", Message = billFundingMessage });
        }

        if (RecurringPayments.Count > 0)
        {
            var subscriptionTotal = RecurringPayments.Sum(r => r.WeeklyAmount);
            var newSubscriptions = RecurringPayments.Count(r => !r.IsAlreadyBill);
            BudgetInsightRows.Add(new BudgetInsightRow
            {
                Title = "Subscriptions",
                Message = $"{RecurringPayments.Count} recurring payment{(RecurringPayments.Count == 1 ? "" : "s")} found, worth about {subscriptionTotal:C}/week.",
                NavTarget = "Subscriptions"
            });
            SubscriptionInsights.Add(new BudgetInsightRow
            {
                Title = "Subscription load",
                Message = $"{RecurringPayments.Count} recurring payment{(RecurringPayments.Count == 1 ? "" : "s")} found, about {subscriptionTotal:C}/week."
            });
            SubscriptionInsights.Add(new BudgetInsightRow
            {
                Title = "Not in bills yet",
                Message = newSubscriptions == 0
                    ? "Every detected recurring payment already looks covered by bills."
                    : $"{newSubscriptions} recurring payment{(newSubscriptions == 1 ? "" : "s")} can still be added as bills."
            });
        }

        if (BudgetPlannerIncomeGap < 0)
        {
            BudgetInsightRows.Add(new BudgetInsightRow
            {
                Title = "Shortfall",
                Message = $"Your weekly plan is {-BudgetPlannerIncomeGap:C} over income before any extra spending.",
                NavTarget = "Budget"
            });
        }
        else
        {
            BudgetInsightRows.Add(new BudgetInsightRow
            {
                Title = "Left to allocate",
                Message = $"{BudgetPlannerIncomeGap:C} remains after planned transfers, essentials, and unplanned spending.",
                NavTarget = "Budget"
            });
        }

        CashFlowInsights.Add(new BudgetInsightRow
        {
            Title = "Payday",
            Message = DaysUntilPayday == 0 ? "Payday is today." : $"{DaysUntilPayday} day{(DaysUntilPayday == 1 ? "" : "s")} until payday."
        });
        CashFlowInsights.Add(new BudgetInsightRow
        {
            Title = "Before payday",
            Message = BillsFundingShortfall > 0
                ? $"Bills savers need {BillsFundingShortfall:C} more before payday."
                : "Bills due before payday look covered by current saver balances."
        });
        CashFlowInsights.Add(new BudgetInsightRow
        {
            Title = "On payday",
            Message = BillsDueOnPaydayCount == 0
                ? "No bills are due on the next payday."
                : $"{BillsDueOnPaydayCount} bill{(BillsDueOnPaydayCount == 1 ? "" : "s")} totalling {BillsDueOnPayday:C} are due on payday."
        });
        CashFlowInsights.Add(new BudgetInsightRow
        {
            Title = "Safe to spend",
            Message = $"{SafeToSpendAmount:C} remains after planned saver transfers and essentials."
        });
        var lowForecast = CashForecastRows.OrderBy(r => r.ProjectedBalance).FirstOrDefault();
        if (lowForecast is not null && lowForecast.ProjectedBalance < 0)
        {
            CashFlowInsights.Add(new BudgetInsightRow
            {
                Title = "Projected shortfall",
                Message = $"Forecast drops to {lowForecast.ProjectedBalance:C} on {lowForecast.Date:dd/MM/yyyy}."
            });
        }

        foreach (var row in BillFundingPlanRows.Where(r => r.NeededBeforePayday > 0).Take(5))
        {
            BillSaverInsights.Add(new BudgetInsightRow
            {
                Title = $"{row.SaverName} needs funding",
                Message = $"Move {row.NeededBeforePayday:C} now, then keep adding {row.WeeklyAmount:C}/week."
            });
        }

        foreach (var row in BillFundingPlanRows.Where(r => r.DueOnPayday > 0).Take(5))
        {
            BillSaverInsights.Add(new BudgetInsightRow
            {
                Title = $"{row.SaverName} payday bills",
                Message = $"{row.DueOnPaydayCount} bill{(row.DueOnPaydayCount == 1 ? "" : "s")} totalling {row.DueOnPayday:C} are due on payday. Transfer {row.PaydayTransfer:C} to this account."
            });
        }

        BudgetHealthInsights.Add(new BudgetInsightRow
        {
            Title = "Budget balance",
            Message = BudgetPlannerIncomeGap < 0
                ? $"Planned weekly money is {-BudgetPlannerIncomeGap:C} above income."
                : $"{BudgetPlannerIncomeGap:C} is left after the weekly plan."
        });
        BudgetHealthInsights.Add(new BudgetInsightRow
        {
            Title = "Planned transfers",
            Message = $"{BudgetPlannerWeeklyTransfers:C}/week goes to bill savers and targets."
        });

        LoadSpendingInsights();
        LoadGoalInsights();
        LoadCleanupInsights();

        AddEmptyInsight(CashFlowInsights, "Cash flow", "Add income, bills, and saver targets to unlock cash flow insights.");
        AddEmptyInsight(BillSaverInsights, "Bills & savers", "Add bills and assign each one to its saver/account.");
        AddEmptyInsight(SubscriptionInsights, "Subscriptions", "Add or import transactions to find recurring payments.");
        AddEmptyInsight(SpendingInsights, "Spending", "Add transactions to compare spending with the previous period.");
        AddEmptyInsight(BudgetHealthInsights, "Budget health", "Make a budget to see whether the weekly plan fits your income.");
        AddEmptyInsight(GoalInsights, "Goals", "Add savings goals or saver targets to track progress.");
        AddEmptyInsight(CleanupInsights, "Account cleanup", "No cleanup prompts right now.");

        SetInsightNavTargets(BillSaverInsights, "Bills");
        SetInsightNavTargets(SubscriptionInsights, "Subscriptions");
        SetInsightNavTargets(SpendingInsights, "Reports");
        SetInsightNavTargets(BudgetHealthInsights, "Budget");
        SetInsightNavTargets(GoalInsights, "Goals");
        SetInsightNavTargets(CleanupInsights, "Transactions");

        LoadDashboardWidgets();
    }

    private void LoadDashboardWidgets()
    {
        LoadPaydayChecklistRows();
        LoadSpendingLeakRows();
        LoadSubscriptionCleanupRows();
        LoadGoalMomentumRows();
        LoadAccountHealthRows();
        LoadDebtAcceleratorRows();

        SetInsightNavTargets(SpendingLeakRows, "Reports");
        SetInsightNavTargets(SubscriptionCleanupRows, "Subscriptions");
        SetInsightNavTargets(GoalMomentumRows, "Goals");
        SetInsightNavTargets(DebtAcceleratorRows, "Debts");

        OnPropertyChanged(nameof(CashRunwaySummary));
        OnPropertyChanged(nameof(UpcomingSqueezeSummary));
    }

    private void LoadPaydayChecklistRows()
    {
        PaydayChecklistRows.Clear();
        PaydayChecklistRows.Add(new BudgetInsightRow
        {
            Title = "Move bill money",
            Message = BillsFundingShortfall > 0
                ? $"Move {BillsFundingShortfall:C} into bill savers before payday."
                : "Bill savers look funded before payday.",
            NavTarget = "Bills"
        });
        PaydayChecklistRows.Add(new BudgetInsightRow
        {
            Title = "Plan payday transfer",
            Message = PaydayTransferTotal > 0
                ? $"Set aside {PaydayTransferTotal:C} on payday across bill saver accounts."
                : "No payday transfer is needed from the current bill plan.",
            NavTarget = "Bills"
        });
        PaydayChecklistRows.Add(new BudgetInsightRow
        {
            Title = "Review recurring payments",
            Message = SubscriptionsNotInBillsCount > 0
                ? $"{SubscriptionsNotInBillsCount} recurring payment{(SubscriptionsNotInBillsCount == 1 ? "" : "s")} can become bills."
                : "Detected recurring payments already look covered.",
            NavTarget = "Subscriptions"
        });
        PaydayChecklistRows.Add(new BudgetInsightRow
        {
            Title = "Debt extra",
            Message = DebtTotal > 0 && DebtStrategyMonthlyExtraPayment <= 0
                ? "Add an extra debt payment to activate payoff acceleration."
                : DebtTotal > 0 ? DebtStrategyExtraSummary : "No active debt payoff task.",
            NavTarget = "Debts"
        });
    }

    private void LoadSpendingLeakRows()
    {
        SpendingLeakRows.Clear();
        using var db = new FinoraDbContext();
        var today = DateTime.Today;
        var currentStart = today.AddDays(-6);
        var previousStart = today.AddDays(-13);
        var transactions = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.Date.Date >= previousStart && t.Date.Date <= today)
            .AsEnumerable()
            .Where(IsSpendingTransaction)
            .ToList();

        var current = transactions
            .Where(t => t.Date.Date >= currentStart)
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .ToDictionary(g => g.Key, g => Math.Abs(g.Sum(t => t.AmountDollars)));
        var previous = transactions
            .Where(t => t.Date.Date < currentStart)
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .ToDictionary(g => g.Key, g => Math.Abs(g.Sum(t => t.AmountDollars)));

        foreach (var row in current
            .Select(kvp => new { Name = kvp.Key, Amount = kvp.Value, Previous = previous.GetValueOrDefault(kvp.Key) })
            .Where(r => r.Amount >= 10 && r.Amount > r.Previous)
            .OrderByDescending(r => r.Amount - r.Previous)
            .Take(4))
        {
            SpendingLeakRows.Add(new BudgetInsightRow
            {
                Title = row.Name,
                Message = $"{row.Amount:C} in 7 days, up {(row.Amount - row.Previous):C} from the prior week."
            });
        }

        AddEmptyInsight(SpendingLeakRows, "No leaks found", "Recent category spending is not above last week's pace.");
    }

    private void LoadSubscriptionCleanupRows()
    {
        SubscriptionCleanupRows.Clear();
        foreach (var row in RecurringPayments.OrderByDescending(r => r.WeeklyAmount).Take(4))
        {
            SubscriptionCleanupRows.Add(new BudgetInsightRow
            {
                Title = row.Name,
                Message = row.IsAlreadyBill
                    ? $"{row.WeeklyAmount:C}/week is already covered by bills."
                    : $"{row.WeeklyAmount:C}/week. Add as a bill or mark it ignored."
            });
        }

        AddEmptyInsight(SubscriptionCleanupRows, "No subscriptions yet", "Import more spending history to detect recurring payments.");
    }

    private void LoadGoalMomentumRows()
    {
        GoalMomentumRows.Clear();
        foreach (var row in SavingsGoals
            .OrderByDescending(g => g.Target > 0 ? g.Current / g.Target : 0)
            .Take(4))
        {
            var message = row.TargetDate is null
                ? $"{row.Progress:P0} saved. Add a date to check pace."
                : row.WeeklyContribution <= 0 && row.Current < row.Target
                    ? $"{row.Progress:P0} saved. Set a weekly contribution to project ETA."
                    : $"{row.Progress:P0} saved. ETA: {row.EstimatedTimeToGoal}.";
            GoalMomentumRows.Add(new BudgetInsightRow { Title = row.Name, Message = message });
        }

        AddEmptyInsight(GoalMomentumRows, "No goals yet", "Add savings goals to track momentum.");
    }

    private void LoadAccountHealthRows()
    {
        AccountHealthRows.Clear();
        foreach (var account in Accounts
            .Where(a => a.NeededNow > 0 || a.TargetBehindAmount > 0 || a.BillsDue > 0)
            .OrderByDescending(a => a.NeededNow)
            .ThenByDescending(a => a.TargetBehindAmount)
            .Take(5))
        {
            var message = account.NeededNow > 0
                ? $"{account.NeededNow:C} needed now. {account.RemainingAfterBillsSummary}"
                : account.TargetBehindAmount > 0
                    ? account.TargetPaceSummary
                    : account.RemainingAfterBillsSummary;
            AccountHealthRows.Add(new BudgetInsightRow { Title = account.Name, Message = message });
        }

        AddEmptyInsight(AccountHealthRows, "Accounts steady", "No saver accounts need attention right now.");
    }

    private void LoadDebtAcceleratorRows()
    {
        DebtAcceleratorRows.Clear();
        var activeDebts = Debts
            .Where(d => d.Balance > 0 && d.IncludeInStrategy)
            .OrderByDescending(d => d.InterestRate ?? 0)
            .ThenBy(d => d.Balance)
            .ToList();

        if (activeDebts.Count == 0)
        {
            AddEmptyInsight(DebtAcceleratorRows, "No active debts", "Add debts to model accelerator options.");
            return;
        }

        var current = SimulateDebtStrategy(activeDebts, DebtStrategyMonthlyExtraPayment, DebtStrategyRollsOverMinimums);
        foreach (var weeklyExtra in new[] { 25m, 50m, 100m })
        {
            var monthlyExtra = DebtStrategyMonthlyExtraPayment + ConvertPaymentToMonthly(weeklyExtra, "Weekly");
            var boosted = SimulateDebtStrategy(activeDebts, monthlyExtra, DebtStrategyRollsOverMinimums);
            var monthsSaved = Math.Max(current.Months - boosted.Months, 0);
            var interestSaved = Math.Max(current.InterestPaid - boosted.InterestPaid, 0);
            DebtAcceleratorRows.Add(new BudgetInsightRow
            {
                Title = $"+{weeklyExtra:C0}/week",
                Message = $"Could save {monthsSaved} month{(monthsSaved == 1 ? "" : "s")} and {interestSaved:C} interest."
            });
        }
    }

    private void LoadSpendingInsights()
    {
        using var db = new FinoraDbContext();
        var (periodStart, periodEnd) = GetSummaryPeriodRange();
        var periodDays = Math.Max((periodEnd.Date - periodStart.Date).Days + 1, 1);
        var previousStart = periodStart.AddDays(-periodDays);
        var previousEnd = periodStart.AddDays(-1);

        var transactions = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.Date.Date >= previousStart.Date && t.Date.Date <= periodEnd.Date)
            .ToList();

        var currentSpending = Math.Abs(transactions
            .Where(t => t.Date.Date >= periodStart.Date && t.Date.Date <= periodEnd.Date)
            .Where(IsSpendingTransaction)
            .Sum(t => t.AmountDollars));
        var previousSpending = Math.Abs(transactions
            .Where(t => t.Date.Date >= previousStart.Date && t.Date.Date <= previousEnd.Date)
            .Where(IsSpendingTransaction)
            .Sum(t => t.AmountDollars));
        var difference = currentSpending - previousSpending;

        SpendingInsights.Add(new BudgetInsightRow
        {
            Title = "Period comparison",
            Message = difference >= 0
                ? $"Spending is {difference:C} higher than the previous {periodDays} days."
                : $"Spending is {-difference:C} lower than the previous {periodDays} days."
        });

        var topCategory = transactions
            .Where(t => t.Date.Date >= periodStart.Date && t.Date.Date <= periodEnd.Date)
            .Where(IsSpendingTransaction)
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .Select(g => new { Name = g.Key, Amount = Math.Abs(g.Sum(t => t.AmountDollars)) })
            .OrderByDescending(g => g.Amount)
            .FirstOrDefault();
        if (topCategory is not null)
        {
            SpendingInsights.Add(new BudgetInsightRow
            {
                Title = "Top category",
                Message = $"{topCategory.Name} is the biggest category this period at {topCategory.Amount:C}."
            });
        }

        var topMerchant = transactions
            .Where(t => t.Date.Date >= periodStart.Date && t.Date.Date <= periodEnd.Date)
            .Where(IsSpendingTransaction)
            .GroupBy(t => NormalizeRecurringDescription(t.Description))
            .Select(g => new { Name = g.Key, Amount = Math.Abs(g.Sum(t => t.AmountDollars)), Count = g.Count() })
            .OrderByDescending(g => g.Amount)
            .FirstOrDefault();
        if (topMerchant is not null)
        {
            SpendingInsights.Add(new BudgetInsightRow
            {
                Title = "Biggest merchant",
                Message = $"{topMerchant.Name} totals {topMerchant.Amount:C} across {topMerchant.Count} transaction{(topMerchant.Count == 1 ? "" : "s")}."
            });
        }
    }

    private void LoadGoalInsights()
    {
        using var db = new FinoraDbContext();
        var goals = db.SavingsGoals.OrderBy(g => g.TargetDate ?? DateTime.MaxValue).ThenBy(g => g.Name).ToList();
        foreach (var goal in goals.Take(5))
        {
            var remaining = Math.Max(goal.TargetDollars - goal.CurrentDollars, 0);
            if (remaining <= 0)
            {
                GoalInsights.Add(new BudgetInsightRow { Title = goal.Name, Message = "Goal complete." });
                continue;
            }

            if (goal.WeeklyContributionDollars <= 0)
            {
                GoalInsights.Add(new BudgetInsightRow { Title = goal.Name, Message = $"Needs {remaining:C}; set a weekly amount to track timing." });
                continue;
            }

            var estimatedDate = DateTime.Today.AddDays((double)Math.Ceiling(remaining / goal.WeeklyContributionDollars) * 7);
            var targetText = goal.TargetDate is null ? "" : $" Target date is {goal.TargetDate.Value:dd/MM/yyyy}.";
            GoalInsights.Add(new BudgetInsightRow
            {
                Title = goal.Name,
                Message = $"At {goal.WeeklyContributionDollars:C}/week, estimated finish is {estimatedDate:dd/MM/yyyy}.{targetText}"
            });
        }

        using var accountDb = new FinoraDbContext();
        var saverTargets = accountDb.Accounts
            .Include(a => a.Transactions)
            .Where(a => a.Type == AccountType.Savings && a.TargetCents != null)
            .OrderBy(a => a.TargetDate ?? DateTime.MaxValue)
            .ThenBy(a => a.Name)
            .Take(5)
            .ToList();

        foreach (var account in saverTargets)
        {
            var balance = account.Transactions.Sum(t => t.AmountDollars);
            var weekly = GetWeeklyAccountTargetContribution(account, balance, NextPayDate);
            GoalInsights.Add(new BudgetInsightRow
            {
                Title = account.Name,
                Message = weekly <= 0
                    ? $"Target is covered or missing a target date. Current balance is {balance:C}."
                    : $"Put {weekly:C}/week into this saver to reach {account.TargetDollars:C} by {account.TargetDate:dd/MM/yyyy}."
            });
        }
    }

    private void LoadCleanupInsights()
    {
        using var db = new FinoraDbContext();
        var recentStart = DateTime.Today.AddDays(-30);
        var uncategorized = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.Date >= recentStart)
            .AsEnumerable()
            .Count(t => IsSpendingTransaction(t) && GetDisplayCategoryName(t.Category?.Name) == "Misc");
        if (uncategorized > 0)
        {
            CleanupInsights.Add(new BudgetInsightRow
            {
                Title = "Categorise transactions",
                Message = $"{uncategorized} spending transaction{(uncategorized == 1 ? "" : "s")} from the last 30 days are still Misc."
            });
        }

        var unassignedBillCount = db.Bills.Count(b => b.AccountId <= 0);
        if (unassignedBillCount > 0)
        {
            CleanupInsights.Add(new BudgetInsightRow
            {
                Title = "Bills without saver",
                Message = $"{unassignedBillCount} bill{(unassignedBillCount == 1 ? "" : "s")} need an account/saver."
            });
        }

        var duplicateBills = db.Bills
            .AsEnumerable()
            .GroupBy(b => new { Name = NormalizeRecurringDescription(b.Name).ToUpperInvariant(), b.AccountId, b.AmountCents })
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateBills.Count > 0)
        {
            CleanupInsights.Add(new BudgetInsightRow
            {
                Title = "Possible duplicate bills",
                Message = $"{duplicateBills.Count} bill group{(duplicateBills.Count == 1 ? "" : "s")} look duplicated."
            });
        }

        var billLikeAdjustments = db.Transactions
            .Include(t => t.Category)
            .Where(t => t.Description == "Up balance adjustment" && t.AmountCents < 0)
            .AsEnumerable()
            .Count(t => db.Bills
                .AsEnumerable()
                .Any(b =>
                    b.AccountId == t.AccountId &&
                    Math.Abs(b.AmountCents - Math.Abs(t.AmountCents)) <= 1 &&
                    Math.Abs((GetClosestBillDueDate(b, t.Date).Date - t.Date.Date).TotalDays) <= 5));
        if (billLikeAdjustments > 0)
        {
            CleanupInsights.Add(new BudgetInsightRow
            {
                Title = "Bill-like adjustments",
                Message = $"{billLikeAdjustments} balance adjustment{(billLikeAdjustments == 1 ? "" : "s")} look like bill payments and can be cleaned up."
            });
        }

        var missingBills = RecurringPayments.Count(r => !r.IsAlreadyBill);
        if (missingBills > 0)
        {
            CleanupInsights.Add(new BudgetInsightRow
            {
                Title = "Recurring not budgeted",
                Message = $"{missingBills} recurring payment{(missingBills == 1 ? "" : "s")} can be added to bills."
            });
        }
    }

    private static void AddEmptyInsight(ObservableCollection<BudgetInsightRow> rows, string title, string message)
    {
        if (rows.Count == 0)
        {
            rows.Add(new BudgetInsightRow { Title = title, Message = message });
        }
    }

    /// <summary>Stamps every row in a tip/insight list with the nav section its "Go to" button should open.</summary>
    private static void SetInsightNavTargets(IEnumerable<BudgetInsightRow> rows, string? navTarget)
    {
        foreach (var row in rows)
        {
            row.NavTarget = navTarget;
        }
    }

    public void SetBudgetBreakdownIncluded(string itemKey, bool isIncluded)
    {
        if (string.IsNullOrWhiteSpace(itemKey))
        {
            return;
        }

        using var db = new FinoraDbContext();
        var normalizedKey = NormalizeBudgetItemKey(itemKey);
        var includedKeys = LoadBudgetIncludedItemKeys(db);
        var excludedKeys = LoadBudgetExcludedItemKeys(db);
        var defaultIncluded = IsDefaultIncludedBudgetItem(normalizedKey);
        if (isIncluded)
        {
            excludedKeys.Remove(normalizedKey);
            if (!defaultIncluded)
            {
                includedKeys.Add(normalizedKey);
            }
        }
        else
        {
            includedKeys.Remove(normalizedKey);
            if (defaultIncluded)
            {
                excludedKeys.Add(normalizedKey);
            }
        }

        SaveBudgetIncludedItemKeys(db, includedKeys);
        SaveBudgetExcludedItemKeys(db, excludedKeys);

        // If the user unticks the savings row, zero the saved savings amount so the tile disappears.
        var savingsKey = NormalizeBudgetItemKey(BuildBudgetItemKey("Savings", SuggestedSavingsBudgetName));
        if (!isIncluded && string.Equals(normalizedKey, savingsKey, StringComparison.OrdinalIgnoreCase))
        {
            var budget = db.WeeklyBudgets.FirstOrDefault();
            if (budget is not null)
            {
                budget.SavingsDollars = 0;
            }
            BudgetSavings = 0;
        }

        db.SaveChanges();
    }

    public void ClearBudgetItems()
    {
        using var db = new FinoraDbContext();
        SaveBudgetIncludedItemKeys(db, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SaveBudgetExcludedItemKeys(db, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        db.SaveChanges();
    }

    public bool AddBillToBudget(int billId)
    {
        using var db = new FinoraDbContext();
        var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null)
        {
            return false;
        }

        AddBudgetItemKey(db, BuildBudgetItemKey("Bills", bill.Name));
        db.SaveChanges();
        return true;
    }

    public bool AddTransactionToBudget(int transactionId)
    {
        using var db = new FinoraDbContext();
        var transaction = db.Transactions
            .Include(t => t.Category)
            .FirstOrDefault(t => t.Id == transactionId);
        if (transaction is null || !IsSpendingTransaction(transaction))
        {
            return false;
        }

        var bucket = GetBudgetBucketForCategory(transaction.Category?.Name);
        var itemName = bucket is "Essentials" or "Bills"
            ? GetDisplayCategoryName(transaction.Category?.Name)
            : (string.IsNullOrWhiteSpace(transaction.Description) ? "Misc spending" : transaction.Description.Trim());

        AddBudgetItemKey(db, BuildBudgetItemKey(bucket, itemName));
        db.SaveChanges();
        return true;
    }

    public bool AddSavingsGoalToBudget(int goalId)
    {
        using var db = new FinoraDbContext();
        var goal = db.SavingsGoals.FirstOrDefault(g => g.Id == goalId);
        if (goal is null)
        {
            return false;
        }

        AddBudgetItemKey(db, BuildBudgetItemKey("Savings", BuildSavingsGoalBudgetName(goal.Name)));
        db.SaveChanges();
        return true;
    }

    private static void RolloverPastAccountTargets()
    {
        using var db = new FinoraDbContext();
        var today = DateTime.Today;

        var pastTargets = db.Accounts
            .Include(a => a.Transactions)
            .Where(a => a.TargetDate != null && a.TargetDate < today)
            .ToList();

        if (pastTargets.Count == 0) return;

        foreach (var account in pastTargets)
        {
            var newDate = account.TargetDate!.Value;
            while (newDate < today)
                newDate = newDate.AddMonths(1);

            var currentBalance = account.Transactions.Sum(t => t.AmountDollars);
            account.TargetDate = newDate;
            account.TargetStartDate = today;
            account.TargetStartingBalanceDollars = currentBalance;
        }

        db.SaveChanges();
    }

    public void RenameBudgetRow(BudgetBreakdownRow row, string newName)
    {
        using var db = new FinoraDbContext();

        if (row.BillId.HasValue)
        {
            var bill = db.Bills.FirstOrDefault(b => b.Id == row.BillId.Value);
            if (bill is null) return;
            bill.Name = newName;
            db.SaveChanges();
            return;
        }

        if (row.AccountId.HasValue)
        {
            var account = db.Accounts.FirstOrDefault(a => a.Id == row.AccountId.Value);
            if (account is null) return;
            account.Name = newName;
            db.SaveChanges();
            return;
        }

        if (row.SavingsGoalId.HasValue)
        {
            var goal = db.SavingsGoals.FirstOrDefault(g => g.Id == row.SavingsGoalId.Value);
            if (goal is null) return;
            goal.Name = newName;
            db.SaveChanges();
        }
    }

    public bool RemoveAccountTarget(int accountId)
    {
        using var db = new FinoraDbContext();
        var account = db.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null)
        {
            return false;
        }

        account.TargetCents = null;
        account.TargetDate = null;
        account.TargetStartDate = null;
        account.TargetStartingBalanceCents = null;
        db.SaveChanges();
        return true;
    }

    public bool AddAccountTargetToBudget(int accountId)
    {
        using var db = new FinoraDbContext();
        var account = db.Accounts.FirstOrDefault(a => a.Id == accountId);
        // Allow any account type with a target — Bills accounts are bill savers that need weekly funding
        if (account is null || account.TargetCents is null)
        {
            return false;
        }

        // Always use "Savings" — this matches the ExclusionKey format the budget breakdown generates
        // for all account target rows (both Savings and Bills type accounts use "Savings::{name}")
        AddBudgetItemKey(db, BuildBudgetItemKey("Savings", BuildAccountBudgetName(account.Name)));
        db.SaveChanges();
        return true;
    }

    /// <summary>
    /// Called by the What-If window when the user applies a scenario.
    /// Saves any custom Savings / Essentials / Unplanned items that aren't
    /// auto-generated from bills or account targets.
    /// </summary>
    public void ApplyWhatIfCustomItems(IEnumerable<(string Category, string Name, decimal Amount, string TransferTo)> whatIfItems)
    {
        using var db = new FinoraDbContext();
        var billNames = db.Bills.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetAccountNames = db.Accounts
            .Where(a => a.TargetCents != null)
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingCustom = LoadCustomBudgetItems(db)
            .Where(c => IsTemplateBudgetItem(c) || IsPlannerSavingsCustomItem(c)) // keep templates / planner items
            .ToList();

        foreach (var item in whatIfItems.Where(i => i.Amount > 0))
        {
            // Skip auto-generated rows: bill rows (Category="Bills"), account target rows (Name="Target"),
            // and rows whose Category is itself an account name (old-style account target rows)
            if (item.Category == "Bills") continue;
            if (item.Name == "Target") continue;
            if (targetAccountNames.Contains(item.Category)) continue;
            if (billNames.Contains(item.Name)) continue;

            var bucket = item.Category is "Essentials" or "Unplanned" ? item.Category : "Savings";
            var key = BuildBudgetItemKey(bucket, item.Name);

            // Replace if exists, otherwise add
            existingCustom.RemoveAll(c =>
                string.Equals(BuildBudgetItemKey(c.Bucket, c.Name), key, StringComparison.OrdinalIgnoreCase));
            existingCustom.Add(new CustomBudgetItem(bucket, item.Name, item.Amount, item.TransferTo));
            AddBudgetItemKey(db, key);
        }

        SaveCustomBudgetItems(db, existingCustom);
        db.SaveChanges();
        LoadBudget();
    }

    public bool AddCustomBudgetItem()
    {
        var name = CustomBudgetName.Trim();
        var bucket = string.IsNullOrWhiteSpace(CustomBudgetBucket) ? "Unplanned" : CustomBudgetBucket.Trim();
        if (!BudgetCategoryOptions.Contains(bucket, StringComparer.OrdinalIgnoreCase))
        {
            bucket = "Unplanned";
        }

        var transferTo = CustomBudgetTransferTo.Trim();
        var amount = RoundDollars(CustomBudgetAmount);
        if (string.IsNullOrWhiteSpace(name) || amount <= 0)
        {
            return false;
        }

        using var db = new FinoraDbContext();
        var items = LoadCustomBudgetItems(db)
            .Where(item => !string.Equals(BuildBudgetItemKey(item.Bucket, item.Name), BuildBudgetItemKey(bucket, name), StringComparison.OrdinalIgnoreCase))
            .ToList();
        items.Add(new CustomBudgetItem(bucket, name, amount, transferTo));
        SaveCustomBudgetItems(db, items);
        AddBudgetItemKey(db, BuildBudgetItemKey(bucket, name));
        db.SaveChanges();

        CustomBudgetName = string.Empty;
        CustomBudgetAmount = 0;
        CustomBudgetTransferTo = string.Empty;
        return true;
    }

    public bool AddSavingsRecommendationToBudget()
    {
        var amount = RoundDollars(SavingsRecommendationAmount);
        if (amount <= 0)
        {
            return false;
        }

        using var db = new FinoraDbContext();
        var items = LoadCustomBudgetItems(db)
            .Where(item => !string.Equals(BuildBudgetItemKey(item.Bucket, item.Name), BuildBudgetItemKey("Savings", SuggestedSavingsBudgetName), StringComparison.OrdinalIgnoreCase))
            .ToList();
        items.Add(new CustomBudgetItem("Savings", SuggestedSavingsBudgetName, amount, string.Empty));
        SaveCustomBudgetItems(db, items);
        AddBudgetItemKey(db, BuildBudgetItemKey("Savings", SuggestedSavingsBudgetName));
        UpsertSetting(db, SavingsBudgetRecommendationDeclinedSettingKey, "false");
        db.SaveChanges();

        _isSavingsRecommendationIgnored = true;
        _isSavingsRecommendationDeclined = false;
        return true;
    }

    public void IgnoreSavingsRecommendation()
    {
        _isSavingsRecommendationIgnored = true;
        OnPropertyChanged(nameof(ShowSavingsRecommendation));
    }

    public void DeclineSavingsRecommendation()
    {
        using var db = new FinoraDbContext();
        UpsertSetting(db, SavingsBudgetRecommendationDeclinedSettingKey, "true");
        db.SaveChanges();

        _isSavingsRecommendationDeclined = true;
        OnPropertyChanged(nameof(ShowSavingsRecommendation));
    }

    private void ClearSavingsRecommendationDecline()
    {
        using var db = new FinoraDbContext();
        UpsertSetting(db, SavingsBudgetRecommendationDeclinedSettingKey, "false");
        db.SaveChanges();

        _isSavingsRecommendationDeclined = false;
        _isSavingsRecommendationIgnored = false;
    }

    public IReadOnlyList<string> GetBudgetTransferAccounts()
    {
        using var db = new FinoraDbContext();
        return db.Accounts
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Name)
            .Select(a => a.Name)
            .ToList();
    }

    public void SetBudgetTransferTarget(string itemKey, string transferTo)
    {
        if (string.IsNullOrWhiteSpace(itemKey) || string.IsNullOrWhiteSpace(transferTo))
        {
            return;
        }

        using var db = new FinoraDbContext();
        var transferTargets = LoadBudgetTransferTargets(db);
        transferTargets[itemKey] = transferTo.Trim();
        SaveBudgetTransferTargets(db, transferTargets);
        db.SaveChanges();
    }

    private static void AddBudgetItemKey(FinoraDbContext db, string itemKey)
    {
        var normalizedKey = NormalizeBudgetItemKey(itemKey);
        var includedKeys = LoadBudgetIncludedItemKeys(db);
        var excludedKeys = LoadBudgetExcludedItemKeys(db);
        excludedKeys.Remove(normalizedKey);
        if (!IsDefaultIncludedBudgetItem(normalizedKey))
        {
            includedKeys.Add(normalizedKey);
        }
        SaveBudgetIncludedItemKeys(db, includedKeys);
        SaveBudgetExcludedItemKeys(db, excludedKeys);
    }

    private static void SaveBudgetIncludedItemKeys(FinoraDbContext db, HashSet<string> includedKeys)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == BudgetIncludedItemsSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = BudgetIncludedItemsSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = string.Join(Environment.NewLine, includedKeys.OrderBy(k => k));
    }

    private static void SaveBudgetExcludedItemKeys(FinoraDbContext db, HashSet<string> excludedKeys)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == BudgetExcludedItemsSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = BudgetExcludedItemsSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = string.Join(Environment.NewLine, excludedKeys.OrderBy(k => k));
    }

    private sealed record CustomBudgetItem(string Bucket, string Name, decimal Amount, string TransferTo);

    private static bool IsTemplateBudgetItem(CustomBudgetItem item)
    {
        return item.Name.StartsWith(TemplateBudgetPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlannerSavingsCustomItem(CustomBudgetItem item)
    {
        if (!string.Equals(item.Bucket, "Savings", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(item.Name, "Template savings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, "Saved targets allocation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, "Budget planner savings", StringComparison.OrdinalIgnoreCase);
    }

    private static List<CustomBudgetItem> LoadCustomBudgetItems(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == CustomBudgetItemsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;

        return value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 4 && decimal.TryParse(parts[2], out _))
            .Select(parts => new CustomBudgetItem(
                parts[0].Trim(),
                parts[1].Trim(),
                RoundDollars(decimal.Parse(parts[2])),
                parts[3].Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Bucket) && !string.IsNullOrWhiteSpace(item.Name) && item.Amount > 0)
            .ToList();
    }

    private static void SaveCustomBudgetItems(FinoraDbContext db, IEnumerable<CustomBudgetItem> items)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == CustomBudgetItemsSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = CustomBudgetItemsSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = string.Join(Environment.NewLine, items
            .OrderBy(item => item.Bucket)
            .ThenBy(item => item.Name)
            .Select(item => $"{item.Bucket}\t{item.Name}\t{item.Amount:0.00}\t{item.TransferTo}"));
    }

    private void LoadBudgetSnapshots()
    {
        BudgetSnapshots.Clear();
        using var db = new FinoraDbContext();
        var value = db.AppSettings
            .Where(s => s.Key == BudgetSnapshotsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;

        foreach (var line in value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 6)
            .Select(parts => new
            {
                Parts = parts,
                IsValid = DateTime.TryParse(parts[0], out _) &&
                    decimal.TryParse(parts[1], out _) &&
                    decimal.TryParse(parts[2], out _) &&
                    decimal.TryParse(parts[3], out _) &&
                    decimal.TryParse(parts[4], out _) &&
                    decimal.TryParse(parts[5], out _)
            })
            .Where(row => row.IsValid)
            .TakeLast(20)
            .Reverse())
        {
            BudgetSnapshots.Add(new BudgetSnapshotRow
            {
                CreatedAt = DateTime.Parse(line.Parts[0]),
                Income = decimal.Parse(line.Parts[1]),
                Bills = decimal.Parse(line.Parts[2]),
                Essentials = decimal.Parse(line.Parts[3]),
                Savings = decimal.Parse(line.Parts[4]),
                Unplanned = decimal.Parse(line.Parts[5])
            });
        }
    }

    private void SaveBudgetSnapshot(FinoraDbContext db, decimal income, decimal bills, decimal essentials, decimal savings, decimal unplanned)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == BudgetSnapshotsSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = BudgetSnapshotsSettingKey };
            db.AppSettings.Add(setting);
        }

        var existing = (setting.Value ?? string.Empty)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(49)
            .ToList();
        existing.Add($"{DateTime.Now:O}\t{income:0.00}\t{bills:0.00}\t{essentials:0.00}\t{savings:0.00}\t{unplanned:0.00}");
        setting.Value = string.Join(Environment.NewLine, existing);
        db.SaveChanges();
        LoadBudgetSnapshots();
    }

    public void RestoreBudgetSnapshot(BudgetSnapshotRow snapshot)
    {
        SaveBudget(snapshot.Income, snapshot.Bills, snapshot.Essentials, snapshot.Savings, snapshot.Unplanned);
    }

    public void LoadTransactionRules()
    {
        TransactionRules.Clear();
        using var db = new FinoraDbContext();
        var transactions = db.Transactions.ToList();
        foreach (var rule in LoadTransactionRules(db))
        {
            TransactionRules.Add(new TransactionRuleRow
            {
                ContainsText = rule.ContainsText,
                CategoryName = rule.CategoryName,
                DisplayName = rule.DisplayName,
                MatchedCount = transactions.Count(t => t.Description.Contains(rule.ContainsText, StringComparison.OrdinalIgnoreCase))
            });
        }

        OnPropertyChanged(nameof(TransactionRuleCategoryOptions));
    }

    public bool AddTransactionRule()
    {
        var contains = NewRuleContainsText.Trim();
        var category = NewRuleCategoryName.Trim();
        var displayName = NewRuleDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(contains) || string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        using var db = new FinoraDbContext();
        if (!db.Categories.Any(c => c.Name == category))
        {
            return false;
        }

        var rules = LoadTransactionRules(db)
            .Where(r => !string.Equals(r.ContainsText, contains, StringComparison.OrdinalIgnoreCase))
            .ToList();
        rules.Add(new TransactionRule(contains, category, displayName));
        SaveTransactionRules(db, rules);
        db.SaveChanges();
        NewRuleContainsText = string.Empty;
        NewRuleCategoryName = string.Empty;
        NewRuleDisplayName = string.Empty;
        LoadTransactionRules();
        return true;
    }

    public int ApplyTransactionRules()
    {
        using var db = new FinoraDbContext();
        var rules = LoadTransactionRules(db);
        var categories = db.Categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var transaction in db.Transactions.Include(t => t.Category).ToList())
        {
            var rule = rules.FirstOrDefault(r => transaction.Description.Contains(r.ContainsText, StringComparison.OrdinalIgnoreCase));
            if (rule is null || !categories.TryGetValue(rule.CategoryName, out var category) || transaction.CategoryId == category.Id)
            {
                continue;
            }

            transaction.CategoryId = category.Id;
            updated++;
        }

        db.SaveChanges();
        LoadDashboard();
        return updated;
    }

    private sealed record TransactionRule(string ContainsText, string CategoryName, string DisplayName = "");

    private static List<TransactionRule> LoadTransactionRules(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == TransactionRulesSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;

        return value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            .Select(parts => new TransactionRule(
                parts[0].Trim(),
                parts[1].Trim(),
                parts.Length >= 3 ? parts[2].Trim() : string.Empty))
            .ToList();
    }

    private static void SaveTransactionRules(FinoraDbContext db, IEnumerable<TransactionRule> rules)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == TransactionRulesSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = TransactionRulesSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = string.Join(Environment.NewLine, rules
            .OrderBy(r => r.ContainsText)
            .Select(r => string.IsNullOrWhiteSpace(r.DisplayName)
                ? $"{r.ContainsText}\t{r.CategoryName}"
                : $"{r.ContainsText}\t{r.CategoryName}\t{r.DisplayName}"));
    }

    // ── Category limits ──────────────────────────────────────────────────────

    private sealed record CategoryLimitData(string Category, decimal WeeklyLimit);

    private static List<CategoryLimitData> LoadCategoryLimitsFromDb(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == CategoryLimitsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<CategoryLimitData>>(value) ?? new(); }
        catch { return new(); }
    }

    private static void SaveCategoryLimitsToDb(FinoraDbContext db, IEnumerable<CategoryLimitData> limits)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == CategoryLimitsSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = CategoryLimitsSettingKey };
            db.AppSettings.Add(setting);
        }
        setting.Value = System.Text.Json.JsonSerializer.Serialize(limits.ToList());
    }

    public void LoadCategoryLimits()
    {
        CategoryLimits.Clear();
        using var db = new FinoraDbContext();
        foreach (var l in LoadCategoryLimitsFromDb(db))
            CategoryLimits.Add(new CategoryLimitRow { Category = l.Category, WeeklyLimit = l.WeeklyLimit });
    }

    public bool AddCategoryLimit()
    {
        if (string.IsNullOrWhiteSpace(NewLimitCategory)) return false;
        if (!decimal.TryParse(NewLimitAmount, out var amount) || amount <= 0) return false;

        using var db = new FinoraDbContext();
        var limits = LoadCategoryLimitsFromDb(db)
            .Where(l => !string.Equals(l.Category, NewLimitCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        limits.Add(new CategoryLimitData(NewLimitCategory, amount));
        SaveCategoryLimitsToDb(db, limits);
        db.SaveChanges();
        NewLimitCategory = string.Empty;
        NewLimitAmount = string.Empty;
        LoadCategoryLimits();
        LoadDailyTracker();
        return true;
    }

    public void DeleteCategoryLimit(string category)
    {
        using var db = new FinoraDbContext();
        var limits = LoadCategoryLimitsFromDb(db)
            .Where(l => !string.Equals(l.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SaveCategoryLimitsToDb(db, limits);
        db.SaveChanges();
        LoadCategoryLimits();
        LoadDailyTracker();
    }

    // ── Transaction search ───────────────────────────────────────────────────

    public void SearchTransactions()
    {
        TransactionSearchResults.Clear();
        var query = TransactionSearchQuery.Trim();
        if (query.Length < 2) return;

        using var db = new FinoraDbContext();
        var results = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.Description.Contains(query))
            .OrderByDescending(t => t.Date)
            .Take(100)
            .AsNoTracking()
            .ToList();

        foreach (var t in results)
        {
            TransactionSearchResults.Add(new TransactionSearchRow
            {
                DateDisplay = t.Date.ToString("dd MMM yyyy"),
                Description = t.Description,
                CategoryName = GetDisplayCategoryName(t),
                AccountName = t.Account?.Name ?? "",
                Amount = t.AmountDollars
            });
        }
    }

    private static BudgetSuggestion FitBudgetToIncome(decimal weeklyIncome, decimal bills, decimal essentials, decimal savings, decimal unplanned, IReadOnlyList<BudgetBreakdownRow> breakdown)
    {
        if (weeklyIncome <= 0)
        {
            return new BudgetSuggestion(0, bills, essentials, savings, unplanned, breakdown);
        }

        var remainingAfterBills = weeklyIncome - bills;
        if (remainingAfterBills <= 0)
        {
            return new BudgetSuggestion(weeklyIncome, bills, 0, 0, 0, ScaleBudgetBreakdown(breakdown, bills, 0, 0, 0));
        }

        var flexibleTotal = essentials + savings + unplanned;
        if (flexibleTotal <= remainingAfterBills)
        {
            return new BudgetSuggestion(weeklyIncome, bills, essentials, savings, unplanned, breakdown);
        }

        var scale = remainingAfterBills / flexibleTotal;
        essentials = RoundDollars(essentials * scale);
        savings = RoundDollars(savings * scale);
        unplanned = RoundDollars(unplanned * scale);

        var overage = bills + essentials + savings + unplanned - weeklyIncome;
        if (overage > 0)
        {
            var unplannedReduction = Math.Min(unplanned, overage);
            unplanned -= unplannedReduction;
            overage -= unplannedReduction;

            var savingsReduction = Math.Min(savings, overage);
            savings -= savingsReduction;
            overage -= savingsReduction;

            essentials = Math.Max(essentials - overage, 0);
        }

        return new BudgetSuggestion(weeklyIncome, bills, essentials, savings, unplanned, ScaleBudgetBreakdown(breakdown, bills, essentials, savings, unplanned));
    }

    private static IReadOnlyList<BudgetBreakdownRow> BuildCategoryBreakdown(
        IEnumerable<Transaction> transactions,
        decimal observedWeeks,
        Func<string?, bool> categoryMatch,
        string bucket,
        string detail)
    {
        return transactions
            .Where(IsSpendingTransaction)
            .Where(t => categoryMatch(t.Category?.Name))
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .Select(g => new BudgetBreakdownRow
            {
                Bucket = bucket,
                Name = g.Key,
                Amount = RoundDollars(g.Sum(t => Math.Abs(t.AmountDollars)) / observedWeeks),
                Detail = detail,
                TransferTo = bucket,
                ExclusionKey = BuildBudgetItemKey(bucket, g.Key)
            })
            .Where(r => r.Amount > 0)
            .OrderByDescending(r => r.Amount)
            .ToList();
    }

    private static IReadOnlyList<BudgetBreakdownRow> BuildDescriptionBreakdown(
        IEnumerable<Transaction> transactions,
        decimal observedWeeks,
        string bucket,
        string detail)
    {
        return transactions
            .Where(IsSpendingTransaction)
            .GroupBy(t => new
            {
                Description = string.IsNullOrWhiteSpace(t.Description) ? "Misc spending" : t.Description.Trim(),
                Category = GetDisplayCategoryName(t.Category?.Name)
            })
            .Select(g => new BudgetBreakdownRow
            {
                Bucket = bucket,
                Name = g.Key.Description,
                Amount = RoundDollars(g.Sum(t => Math.Abs(t.AmountDollars)) / observedWeeks),
                Detail = $"{g.Key.Category} - {detail}",
                TransferTo = bucket,
                ExclusionKey = BuildBudgetItemKey(bucket, g.Key.Description)
            })
            .Where(r => r.Amount > 0)
            .OrderByDescending(r => r.Amount)
            .ToList();
    }

    private static int GetBudgetBucketSortOrder(string bucket)
    {
        return bucket switch
        {
            "Bills" => 0,
            "Essentials" => 1,
            "Savings" => 3,
            "Unplanned" => 4,
            _ => 4
        };
    }

    private static int GetBudgetGroupSortOrder(IEnumerable<BudgetBreakdownRow> rows)
    {
        return rows.Min(row => IsAccountTargetBudgetRow(row) ? 2 : GetBudgetBucketSortOrder(row.Bucket));
    }

    private static IReadOnlyList<BudgetBreakdownRow> ScaleBudgetBreakdown(IReadOnlyList<BudgetBreakdownRow> rows, decimal bills, decimal essentials, decimal savings, decimal unplanned)
    {
        var targets = new Dictionary<string, decimal>
        {
            ["Bills"] = bills,
            ["Essentials"] = essentials,
            ["Savings"] = savings,
            ["Unplanned"] = unplanned
        };

        return rows
            .Select(row =>
            {
                if (!row.IsIncluded)
                {
                    return row;
                }

                var budgetBucket = IsAccountTargetBudgetRow(row) ? "Savings" : row.Bucket;
                var sourceTotal = rows
                    .Where(r => (budgetBucket == "Savings" ? r.Bucket == "Savings" || IsAccountTargetBudgetRow(r) : r.Bucket == row.Bucket) && r.IsIncluded)
                    .Sum(r => r.Amount);
                var targetTotal = targets.GetValueOrDefault(budgetBucket, sourceTotal);
                var amount = sourceTotal <= 0 ? targetTotal : RoundDollars(row.Amount * targetTotal / sourceTotal);
                return new BudgetBreakdownRow
                {
                    Bucket = row.Bucket,
                    Name = row.Name,
                    Amount = amount,
                    Detail = row.Detail,
                    TransferTo = row.TransferTo,
                    ExclusionKey = row.ExclusionKey,
                    IsDefaultIncluded = row.IsDefaultIncluded,
                    IsIncluded = row.IsIncluded
                };
            })
            .Where(r => r.Amount > 0 || !r.IsIncluded)
            .ToList();
    }

    private static void ApplyBudgetInclusions(IEnumerable<BudgetBreakdownRow> rows, HashSet<string> includedKeys, HashSet<string>? excludedKeys = null)
    {
        foreach (var row in rows)
        {
            var normalized = NormalizeBudgetItemKey(row.ExclusionKey);
            row.IsIncluded = IsDefaultIncludedBudgetItem(normalized)
                ? excludedKeys == null || !excludedKeys.Contains(normalized)
                : includedKeys.Contains(normalized);
        }
    }

    private static bool IsAccountTargetBudgetRow(BudgetBreakdownRow row)
    {
        return row.ExclusionKey.StartsWith("Savings::", StringComparison.OrdinalIgnoreCase)
            && row.Name == "Target"
            && row.TransferTo == row.Bucket;
    }

    private static HashSet<string> LoadBudgetIncludedItemKeys(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == BudgetIncludedItemsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;

        return value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeBudgetItemKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> LoadBudgetExcludedItemKeys(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == BudgetExcludedItemsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;

        return value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeBudgetItemKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> LoadBudgetTransferTargets(FinoraDbContext db)
    {
        var value = db.AppSettings
            .Where(s => s.Key == BudgetTransferTargetsSettingKey)
            .Select(s => s.Value)
            .FirstOrDefault() ?? string.Empty;

        return value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            .Select(parts => new { Key = NormalizeBudgetItemKey(parts[0]), TransferTo = parts[1] })
            .GroupBy(parts => parts.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().TransferTo, StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveBudgetTransferTargets(FinoraDbContext db, Dictionary<string, string> transferTargets)
    {
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == BudgetTransferTargetsSettingKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = BudgetTransferTargetsSettingKey };
            db.AppSettings.Add(setting);
        }

        setting.Value = string.Join(Environment.NewLine, transferTargets
            .Where(t => !string.IsNullOrWhiteSpace(t.Key) && !string.IsNullOrWhiteSpace(t.Value))
            .OrderBy(t => t.Key)
            .Select(t => $"{t.Key}\t{t.Value.Trim()}"));
    }

    private static void ApplyBudgetTransferTargets(IEnumerable<BudgetBreakdownRow> rows, Dictionary<string, string> transferTargets)
    {
        foreach (var row in rows)
        {
            if (transferTargets.TryGetValue(row.ExclusionKey, out var transferTo))
            {
                row.TransferTo = transferTo;
            }
        }
    }

    private static string BuildBudgetItemKey(string bucket, string name)
    {
        return NormalizeBudgetItemKey($"{bucket.Trim()}::{name.Trim()}");
    }

    private static bool IsDefaultIncludedBudgetItem(string itemKey)
    {
        if (itemKey.StartsWith("Bills::", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(itemKey, "Savings::Budget planner savings", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(itemKey, "Savings::Template savings", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(itemKey, "Savings::Saved targets allocation", StringComparison.OrdinalIgnoreCase)) return true;
        // Account target rows use "Savings::<accountname>" keys (without "Goal:" prefix).
        // Auto-include them so savings accounts with targets appear in the budget without manual setup.
        if (itemKey.StartsWith("Savings::", StringComparison.OrdinalIgnoreCase) &&
            !itemKey.StartsWith("Savings::Goal:", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string NormalizeBudgetItemKey(string itemKey)
    {
        const string legacySaverPrefix = "Savings::Saver: ";
        return itemKey.Trim().StartsWith(legacySaverPrefix, StringComparison.OrdinalIgnoreCase)
            ? $"Savings::{itemKey.Trim()[legacySaverPrefix.Length..]}"
            : itemKey.Trim();
    }

    private static string BuildSavingsGoalBudgetName(string name)
    {
        return $"Goal: {name.Trim()}";
    }

    private static string BuildAccountBudgetName(string name)
    {
        return name.Trim();
    }

    private static decimal GetWeeklyAccountTargetContribution(Account account, decimal balance, DateTime nextPayDate)
    {
        if (account.TargetDollars is null || account.TargetDollars <= balance || account.TargetDate is null)
        {
            return 0;
        }

        var payDate = nextPayDate.Date;
        while (payDate < DateTime.Today)
        {
            payDate = payDate.AddDays(7);
        }

        var payPeriodsRemaining = 0;
        while (payDate < account.TargetDate.Value.Date)
        {
            payPeriodsRemaining++;
            payDate = payDate.AddDays(7);
        }

        payPeriodsRemaining = Math.Max(payPeriodsRemaining, 1);
        var remaining = account.TargetDollars.Value - balance;
        return RoundDollars(remaining / payPeriodsRemaining);
    }

    private static string GetBudgetBucketForCategory(string? categoryName)
    {
        if (IsBillCategory(categoryName))
        {
            return "Bills";
        }

        if (IsEssentialCategory(categoryName))
        {
            return "Essentials";
        }

        return "Unplanned";
    }

    private static decimal GetWeeklyBillAmount(Bill bill)
    {
        // Use simple division so "monthly / 4" matches user expectations.
        // The 12/52 exact formula gives lower weekly amounts that feel wrong
        // when people mentally check: monthly bill ÷ 4 weeks.
        return bill.Frequency switch
        {
            BillFrequency.Weekly      => bill.AmountDollars,
            BillFrequency.Fortnightly => bill.AmountDollars / 2m,
            BillFrequency.Monthly     => bill.AmountDollars / 4m,
            BillFrequency.Quarterly   => bill.AmountDollars / 12m,
            BillFrequency.Yearly      => bill.AmountDollars / 52m,
            _                         => bill.AmountDollars / 4m
        };
    }

    private static bool IsEssentialCategory(string? categoryName)
    {
        return categoryName is "Groceries" or "Fuel" or "Medical" or "Study";
    }

    private static bool IsBillCategory(string? categoryName)
    {
        return categoryName is "Rent" or "Phone" or "Internet" or "Car Loan" or "Insurance" or "Debt";
    }

    private static decimal RoundDollars(decimal value)
    {
        return Math.Round(Math.Max(value, 0), 2, MidpointRounding.AwayFromZero);
    }

    private static decimal RoundSignedDollars(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public void LoadDebts()
    {
        Debts.Clear();

        using var db = new FinoraDbContext();
        var paidByDebtId = db.DebtPayments
            .GroupBy(p => p.DebtId)
            .Select(g => new { DebtId = g.Key, PaidCents = g.Sum(p => p.AmountCents) })
            .ToDictionary(g => g.DebtId, g => g.PaidCents / 100m);

        foreach (var debt in db.Debts.OrderByDescending(d => d.BalanceCents).ToList())
        {
            Debts.Add(new DebtRow
            {
                Id = debt.Id,
                Name = debt.Name,
                Balance = debt.BalanceDollars,
                MinimumPayment = debt.MinimumPaymentDollars,
                PaymentPeriod = NormalizeDebtPaymentPeriod(debt.PaymentPeriod),
                InterestRate = debt.InterestRate,
                OriginalBalance = debt.OriginalBalanceDollars,
                RecordedPaid = paidByDebtId.GetValueOrDefault(debt.Id),
                UpPaymentMatchText = debt.UpPaymentMatchText ?? ""
            });
        }

        DebtTotal = Debts.Sum(d => d.Balance);
        LoadDebtPayoffPlan();
        RefreshDebtSummary();
    }

    private void LoadDebtPayoffPlan()
    {
        DebtPayoffPlanRows.Clear();
        foreach (var debt in Debts.OrderByDescending(d => d.Balance))
        {
            var monthlyPayment = ConvertPaymentToMonthly(debt.MinimumPayment, debt.PaymentPeriod);
            var payoff = CalculateDebtPayoff(debt.Balance, monthlyPayment, debt.InterestRate);
            DebtPayoffPlanRows.Add(new DebtPayoffPlanRow
            {
                DebtId = debt.Id,
                Name = debt.Name,
                Balance = debt.Balance,
                MinimumPayment = debt.MinimumPayment,
                InterestRate = debt.InterestRate,
                PaymentPeriod = debt.PaymentPeriod,
                MonthsRemaining = payoff.Months,
                PaymentCoversInterest = payoff.PaymentCoversInterest,
                EstimatedPaidOff = payoff.Months <= 0 || !payoff.PaymentCoversInterest ? null : DateTime.Today.AddMonths(payoff.Months)
            });
        }

        OnPropertyChanged(nameof(NextDebtPayoffSummary));
    }

    public void SetDebtPaymentPeriod(int debtId, string paymentPeriod)
    {
        paymentPeriod = NormalizeDebtPaymentPeriod(paymentPeriod);

        using var db = new FinoraDbContext();
        var debt = db.Debts.FirstOrDefault(d => d.Id == debtId);
        if (debt is null)
        {
            return;
        }

        debt.PaymentPeriod = paymentPeriod;
        db.SaveChanges();
        LoadDebts();
    }

    public void LoadDebtStrategiesForCurrentSelection()
    {
        LoadDebtStrategies();
    }

    public void SetAllDebtStrategySelections(bool isSelected)
    {
        foreach (var debt in Debts.Where(d => d.Balance > 0))
        {
            debt.IncludeInStrategy = isSelected;
        }

        LoadDebtStrategies();
    }

    private static string NormalizeDebtPaymentPeriod(string? paymentPeriod)
    {
        return paymentPeriod is "Weekly" or "Fortnightly" or "Monthly"
            ? paymentPeriod
            : "Weekly";
    }

    private void RefreshDebtSummary()
    {
        OnPropertyChanged(nameof(DebtOriginalTotal));
        OnPropertyChanged(nameof(DebtPaidTotal));
        OnPropertyChanged(nameof(DebtProgressPercent));
        OnPropertyChanged(nameof(DebtProgressSummary));
        OnPropertyChanged(nameof(HighestInterestDebtSummary));
        OnPropertyChanged(nameof(NextDebtPayoffSummary));
    }

    public void LoadDebtPaymentAudit()
    {
        DebtPaymentAuditRows.Clear();
        using var db = new FinoraDbContext();
        var rows = db.DebtPayments
            .Include(p => p.Debt)
            .OrderByDescending(p => p.PaidOn)
            .ThenByDescending(p => p.Id)
            .Take(80)
            .ToList();

        foreach (var payment in rows)
        {
            DebtPaymentAuditRows.Add(new DebtPaymentAuditRow
            {
                PaidOn = payment.PaidOn,
                DebtName = payment.Debt?.Name ?? "Debt",
                Source = payment.UpTransactionId.StartsWith("bill:", StringComparison.OrdinalIgnoreCase) ? "Bill" : "Transaction",
                Amount = payment.AmountDollars,
                Description = payment.Description
            });
        }
    }

    private void LoadDebtStrategies()
    {
        var monthlyExtra = ConvertPaymentToMonthly(DebtStrategyExtraPayment, DebtStrategyExtraPaymentPeriod);
        var extraDisplay = DebtStrategyExtraPayment;
        var rollsOver = DebtStrategyRollsOverMinimums;
        DebtStrategyRows.Clear();
        DebtStrategyRows.Add(BuildDebtStrategyRow("Avalanche",
            Debts.OrderByDescending(d => d.InterestRate ?? 0).ThenBy(d => d.Balance).ToList(),
            extraDisplay, monthlyExtra, rollsOver));
        DebtStrategyRows.Add(BuildDebtStrategyRow("Snowball",
            Debts.OrderBy(d => d.Balance).ThenByDescending(d => d.InterestRate ?? 0).ToList(),
            extraDisplay, monthlyExtra, rollsOver));
        NotifyDebtStrategyProperties();
    }

    public async Task LoadDebtStrategiesAsync()
    {
        var avalancheDebts = Debts.OrderByDescending(d => d.InterestRate ?? 0).ThenBy(d => d.Balance).ToList();
        var snowballDebts = Debts.OrderBy(d => d.Balance).ThenByDescending(d => d.InterestRate ?? 0).ToList();
        var monthlyExtra = ConvertPaymentToMonthly(DebtStrategyExtraPayment, DebtStrategyExtraPaymentPeriod);
        var extraDisplay = DebtStrategyExtraPayment;
        var rollsOver = DebtStrategyRollsOverMinimums;

        var (avRow, sbRow) = await Task.Run(() => (
            BuildDebtStrategyRow("Avalanche", avalancheDebts, extraDisplay, monthlyExtra, rollsOver),
            BuildDebtStrategyRow("Snowball", snowballDebts, extraDisplay, monthlyExtra, rollsOver)
        ));

        DebtStrategyRows.Clear();
        DebtStrategyRows.Add(avRow);
        DebtStrategyRows.Add(sbRow);
        NotifyDebtStrategyProperties();
    }

    private void NotifyDebtStrategyProperties()
    {
        OnPropertyChanged(nameof(DebtStrategyActiveCount));
        OnPropertyChanged(nameof(DebtStrategyMonthlyExtraPayment));
        OnPropertyChanged(nameof(DebtStrategySelectedBalance));
        OnPropertyChanged(nameof(DebtStrategySelectedMonthlyMinimums));
        OnPropertyChanged(nameof(DebtStrategyExcludedBalance));
        OnPropertyChanged(nameof(DebtStrategyExcludedSummary));
        OnPropertyChanged(nameof(DebtStrategyHighestSelectedRateSummary));
        OnPropertyChanged(nameof(DebtStrategyActiveSummary));
        OnPropertyChanged(nameof(DebtStrategyExtraSummary));
        OnPropertyChanged(nameof(DebtStrategySelectedBalanceSummary));
        OnPropertyChanged(nameof(DebtStrategyPaymentPoolSummary));
        OnPropertyChanged(nameof(DebtStrategyRolloverSummary));
        OnPropertyChanged(nameof(BestDebtStrategyTitle));
        OnPropertyChanged(nameof(BestDebtStrategySummary));
        RefreshDebtFreeTarget();
    }

    private void RefreshDebtFreeTarget()
    {
        OnPropertyChanged(nameof(DebtFreeTargetExtraSummary));
    }

    private static DebtStrategyRow BuildDebtStrategyRow(
        string strategy,
        IReadOnlyList<DebtRow> orderedDebts,
        decimal extraPaymentDisplay,
        decimal monthlyExtraPayment,
        bool rollsOverMinimums)
    {
        var activeDebts = orderedDebts.Where(d => d.Balance > 0 && d.IncludeInStrategy).ToList();
        if (activeDebts.Count == 0)
        {
            return new DebtStrategyRow { Strategy = strategy, FirstTarget = "No active debts", ExtraPayment = extraPaymentDisplay };
        }

        var monthlyPaymentPool = activeDebts.Sum(d => ConvertPaymentToMonthly(d.MinimumPayment, d.PaymentPeriod)) + monthlyExtraPayment;
        var payoff = SimulateDebtStrategy(activeDebts, monthlyExtraPayment, rollsOverMinimums);
        return new DebtStrategyRow
        {
            Strategy = strategy,
            FirstTarget = activeDebts[0].Name,
            Order = string.Join(" -> ", activeDebts.Select(d => d.Name)),
            ExtraPayment = extraPaymentDisplay,
            Principal = RoundDollars(activeDebts.Sum(d => d.Balance)),
            MonthlyPaymentPool = RoundDollars(monthlyPaymentPool),
            DebtCount = activeDebts.Count,
            RollsOverMinimums = rollsOverMinimums,
            MonthsRemaining = payoff.Months,
            InterestPaid = payoff.InterestPaid,
            InterestBreakdown = payoff.InterestBreakdown
        };
    }

    private static (int Months, decimal InterestPaid, string InterestBreakdown) SimulateDebtStrategy(IReadOnlyList<DebtRow> debts, decimal extraPayment, bool rollOverMinimums)
    {
        var balances = debts.ToDictionary(d => d.Id, d => d.Balance);
        var interestByDebt = debts.ToDictionary(d => d.Id, _ => 0m);
        var redirectedMinimums = 0m;
        var months = 0;
        var interestPaid = 0m;

        while (balances.Values.Any(balance => balance > 0.005m) && months < 1200)
        {
            months++;
            foreach (var debt in debts)
            {
                var balance = balances[debt.Id];
                if (balance <= 0)
                {
                    continue;
                }

                var monthlyRate = (debt.InterestRate ?? 0) <= 0 ? 0 : debt.InterestRate!.Value / 100m / 12m;
                var interest = balance * monthlyRate;
                interestPaid += interest;
                interestByDebt[debt.Id] += interest;
                balances[debt.Id] = balance + interest;
            }

            var target = debts.FirstOrDefault(d => balances[d.Id] > 0.005m);
            var previouslyActiveIds = debts
                .Where(d => balances[d.Id] > 0.005m)
                .Select(d => d.Id)
                .ToHashSet();
            foreach (var debt in debts.Where(d => balances[d.Id] > 0.005m))
            {
                var payment = ConvertPaymentToMonthly(debt.MinimumPayment, debt.PaymentPeriod);
                if (target is not null && debt.Id == target.Id)
                {
                    payment += extraPayment + redirectedMinimums;
                }

                balances[debt.Id] = Math.Max(balances[debt.Id] - payment, 0);
            }

            foreach (var debt in debts.Where(d => previouslyActiveIds.Contains(d.Id) && balances[d.Id] <= 0.005m))
            {
                if (rollOverMinimums)
                {
                    redirectedMinimums += ConvertPaymentToMonthly(debt.MinimumPayment, debt.PaymentPeriod);
                }
            }
        }

        var breakdown = string.Join(", ", debts
            .Select(d => new { d.Name, Interest = RoundDollars(interestByDebt[d.Id]) })
            .Where(d => d.Interest > 0.005m)
            .OrderByDescending(d => d.Interest)
            .Select(d => $"{d.Name} {d.Interest:C}"));

        return months >= 1200
            ? (0, RoundDollars(interestPaid), breakdown)
            : (months, RoundDollars(interestPaid), breakdown);
    }

    private static decimal? EstimateRequiredMonthlyExtra(IReadOnlyList<DebtRow> debts, int targetMonths, bool rollOverMinimums)
    {
        var orderedDebts = debts.OrderByDescending(d => d.InterestRate ?? 0).ThenBy(d => d.Balance).ToList();
        var low = 0m;
        var high = Math.Max(orderedDebts.Sum(d => d.Balance), 100m);

        for (var attempts = 0; attempts < 20; attempts++)
        {
            var result = SimulateDebtStrategy(orderedDebts, high, rollOverMinimums);
            if (result.Months > 0 && result.Months <= targetMonths)
            {
                break;
            }

            high *= 2;
        }

        var highResult = SimulateDebtStrategy(orderedDebts, high, rollOverMinimums);
        if (highResult.Months <= 0 || highResult.Months > targetMonths)
        {
            return null;
        }

        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) / 2m;
            var result = SimulateDebtStrategy(orderedDebts, mid, rollOverMinimums);
            if (result.Months > 0 && result.Months <= targetMonths)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return Math.Ceiling(high * 100m) / 100m;
    }

    public void LoadDangerAlerts()
    {
        DangerAlerts.Clear();
        var lowForecast = CashForecastRows.OrderBy(r => r.ProjectedBalance).FirstOrDefault();
        if (lowForecast is not null && lowForecast.ProjectedBalance < 0)
        {
            DangerAlerts.Add(new DangerAlertRow
            {
                Title = "Projected shortfall",
                Severity = "Danger",
                Message = $"Cash forecast drops to {lowForecast.ProjectedBalance:C} on {lowForecast.Date:dd/MM/yyyy}."
            });
        }

        foreach (var account in Accounts.Where(a => a.NeededNow > 0).OrderByDescending(a => a.NeededNow).Take(6))
        {
            DangerAlerts.Add(new DangerAlertRow
            {
                Title = $"{account.Name} needs funding",
                Severity = "Warning",
                Message = $"{account.NeededNow:C} needed before payday to cover bills."
            });
        }

        var billDueToday = BillsDueNext7Days
            .Where(b => b.DueDate.Date == DateTime.Today)
            .OrderByDescending(b => b.Amount)
            .FirstOrDefault();
        if (billDueToday is not null)
        {
            DangerAlerts.Add(new DangerAlertRow
            {
                Title = "Bill due today",
                Severity = "Warning",
                Message = $"{billDueToday.Name} needs {billDueToday.Amount:C} today."
            });
        }

        if (SubscriptionsNotInBillsCount > 0)
        {
            DangerAlerts.Add(new DangerAlertRow
            {
                Title = "Subscriptions need review",
                Severity = "Warning",
                Message = $"{SubscriptionsNotInBillsCount} recurring payment{(SubscriptionsNotInBillsCount == 1 ? "" : "s")} are not in bills yet."
            });
        }

        if (DebtTotal > 0 && DebtStrategyMonthlyExtraPayment <= 0)
        {
            DangerAlerts.Add(new DangerAlertRow
            {
                Title = "Debt plan idle",
                Severity = "Warning",
                Message = "Add an extra payment to compare payoff acceleration."
            });
        }

        if (DangerAlerts.Count == 0)
        {
            DangerAlerts.Add(new DangerAlertRow
            {
                Title = "No immediate danger",
                Severity = "Good",
                Message = "Forecast and bill saver balances look okay right now."
            });
        }
    }

    private static decimal ConvertPaymentToMonthly(decimal payment, string period)
    {
        return period switch
        {
            "Weekly" => payment * 52m / 12m,
            "Fortnightly" => payment * 26m / 12m,
            _ => payment
        };
    }

    private static (int Months, bool PaymentCoversInterest) CalculateDebtPayoff(decimal balance, decimal monthlyPayment, decimal? annualInterestRate)
    {
        if (balance <= 0)
        {
            return (0, true);
        }

        if (monthlyPayment <= 0)
        {
            return (0, false);
        }

        var annualRate = annualInterestRate.GetValueOrDefault();
        var monthlyRate = annualRate <= 0 ? 0 : annualRate / 100m / 12m;
        if (monthlyRate <= 0)
        {
            return ((int)Math.Ceiling(balance / monthlyPayment), true);
        }

        if (monthlyPayment <= balance * monthlyRate)
        {
            return (0, false);
        }

        var months = 0;
        var remaining = balance;
        while (remaining > 0.005m && months < 1200)
        {
            remaining += remaining * monthlyRate;
            remaining -= monthlyPayment;
            months++;
        }

        return months >= 1200 ? (0, false) : (months, true);
    }

    public void LoadSavingsGoals()
    {
        SavingsGoals.Clear();

        using var db = new FinoraDbContext();
        foreach (var goal in db.SavingsGoals.OrderBy(g => g.Name).ToList())
        {
            SavingsGoals.Add(new SavingsGoalRow
            {
                Id = goal.Id,
                Name = goal.Name,
                Target = goal.TargetDollars,
                Current = goal.CurrentDollars,
                WeeklyContribution = goal.WeeklyContributionDollars,
                TargetDate = goal.TargetDate
            });
        }
    }

    public void LoadReports(IReadOnlyCollection<Transaction>? loadedTransactions = null, bool refreshRecurring = true)
    {
        SpendingBillRatioRows.Clear();
        CategorySpendingRows.Clear();
        MerchantSpendingRows.Clear();
        IncomeCategoryRows.Clear();
        MonthlyCashFlowRows.Clear();

        using var db = new FinoraDbContext();
        var transactions = loadedTransactions?.ToList()
            ?? db.Transactions
                .Include(t => t.Category)
                .ToList();

        var (periodStart, periodEnd) = GetSummaryPeriodRange();
        var periodTransactions = transactions
            .Where(t => t.Date.Date >= periodStart.Date && t.Date.Date <= periodEnd.Date)
            .Where(IsSpendingTransaction)
            .ToList();

        var totalSpending = Math.Abs(periodTransactions.Sum(t => t.AmountDollars));
        var billsDue = GetVisibleBillOccurrences(db, db.Bills.ToList(), periodStart, periodEnd)
            .Sum(o => o.Bill.AmountDollars);
        var otherSpending = Math.Max(totalSpending - billsDue, 0);
        var ratioTotal = billsDue + otherSpending;

        AddReportRow(SpendingBillRatioRows, "Bills due", billsDue, ratioTotal, "#2563EB");
        AddReportRow(SpendingBillRatioRows, "Other spending", otherSpending, ratioTotal, "#DC2626");

        var categoryTotals = periodTransactions
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .Select(g => new
            {
                Name = g.Key,
                Amount = Math.Abs(g.Sum(t => t.AmountDollars))
            })
            .Where(g => g.Amount > 0)
            .OrderByDescending(g => g.Amount)
            .Take(8)
            .ToList();

        var maxCategory = categoryTotals.Count == 0 ? 0 : categoryTotals.Max(c => c.Amount);
        foreach (var category in categoryTotals)
        {
            AddReportRow(CategorySpendingRows, category.Name, category.Amount, maxCategory, "#0F766E");
        }

        var merchantTotals = periodTransactions
            .GroupBy(t => NormalizeRecurringDescription(t.Description))
            .Select(g => new
            {
                Name = string.IsNullOrWhiteSpace(g.Key) ? "Unknown merchant" : g.Key,
                Amount = Math.Abs(g.Sum(t => t.AmountDollars))
            })
            .Where(g => g.Amount > 0)
            .OrderByDescending(g => g.Amount)
            .Take(8)
            .ToList();

        var maxMerchant = merchantTotals.Count == 0 ? 0 : merchantTotals.Max(c => c.Amount);
        foreach (var merchant in merchantTotals)
        {
            AddReportRow(MerchantSpendingRows, merchant.Name, merchant.Amount, maxMerchant, "#F59E0B");
        }

        var incomeTotals = transactions
            .Where(t => t.Date.Date >= periodStart.Date && t.Date.Date <= periodEnd.Date)
            .Where(IsIncomeTransaction)
            .GroupBy(t => GetDisplayCategoryName(t.Category?.Name))
            .Select(g => new
            {
                Name = g.Key,
                Amount = g.Sum(t => t.AmountDollars)
            })
            .Where(g => g.Amount > 0)
            .OrderByDescending(g => g.Amount)
            .Take(8)
            .ToList();

        var maxIncome = incomeTotals.Count == 0 ? 0 : incomeTotals.Max(c => c.Amount);
        foreach (var income in incomeTotals)
        {
            AddReportRow(IncomeCategoryRows, income.Name, income.Amount, maxIncome, "#34D399");
        }

        var monthlyCashFlow = transactions
            .Where(t => t.Date.Date >= DateTime.Today.AddMonths(-5).Date)
            .Where(t => IsIncomeTransaction(t) || IsSpendingTransaction(t))
            .GroupBy(t => new DateTime(t.Date.Year, t.Date.Month, 1))
            .Select(g => new
            {
                Month = g.Key,
                Amount = g.Where(IsIncomeTransaction).Sum(t => t.AmountDollars) -
                    Math.Abs(g.Where(IsSpendingTransaction).Sum(t => t.AmountDollars))
            })
            .OrderBy(g => g.Month)
            .ToList();

        var maxCashFlow = monthlyCashFlow.Count == 0 ? 0 : monthlyCashFlow.Max(m => Math.Abs(m.Amount));
        foreach (var month in monthlyCashFlow)
        {
            AddReportRow(
                MonthlyCashFlowRows,
                month.Month.ToString("MMM yyyy"),
                RoundSignedDollars(month.Amount),
                maxCashFlow,
                month.Amount >= 0 ? "#34D399" : "#F87171");
        }

        if (refreshRecurring)
        {
            LoadRecurringPayments(transactions);
        }
    }

    private static void AddReportRow(ObservableCollection<ReportChartRow> rows, string label, decimal amount, decimal total, string colorHex)
    {
        rows.Add(new ReportChartRow
        {
            Label = label,
            Amount = amount,
            Share = total <= 0 ? 0 : (double)(Math.Abs(amount) / total),
            ColorHex = colorHex
        });
    }

    public void LoadRecurringPayments(IReadOnlyCollection<Transaction>? loadedTransactions = null)
    {
        // All heavy computation runs on whichever thread this is called from.
        using var db = new FinoraDbContext();
        var transactions = loadedTransactions?.ToList()
            ?? db.Transactions
                .Include(t => t.Category)
                .ToList();

        var recurring = transactions
            .Where(IsSpendingTransaction)
            .Where(t => !string.IsNullOrWhiteSpace(t.Description))
            .GroupBy(t => new
            {
                Name = NormalizeRecurringDescription(t.Description)
            })
            .Where(g => g.Count() >= 2)
            .Select(g => BuildRecurringPaymentRow(g.OrderBy(t => t.Date).ToList()))
            .Where(row => row is not null)
            .OrderBy(row => row!.NextExpected)
            .ThenBy(row => row!.Name)
            .Take(30)
            .ToList();

        var bills = db.Bills.ToList();
        foreach (var row in recurring.Where(r => r is not null))
        {
            row!.IsAlreadyBill = bills.Any(b =>
                string.Equals(NormalizeRecurringDescription(b.Name), row.Name, StringComparison.OrdinalIgnoreCase));
        }

        var finalRows = recurring
            .Where(r => r is not null && !_ignoredSubscriptions.Contains(r!.Name))
            .ToList();

        // Collection update must be on the UI thread.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            RecurringPayments.Clear();
            foreach (var row in finalRows) RecurringPayments.Add(row!);
        }
        else
        {
            dispatcher.Invoke(() =>
            {
                RecurringPayments.Clear();
                foreach (var row in finalRows) RecurringPayments.Add(row!);
            });
        }
    }

    public void IgnoreSubscription(RecurringPaymentRow row)
    {
        _ignoredSubscriptions.Add(row.Name);
        using var db = new FinoraDbContext();
        UpsertSetting(db, IgnoredSubscriptionsSettingKey, System.Text.Json.JsonSerializer.Serialize(_ignoredSubscriptions.ToList()));
        db.SaveChanges();
        RecurringPayments.Remove(row);
    }

    public int DeleteSubscriptionTransactions(RecurringPaymentRow row)
    {
        var normalizedName = NormalizeRecurringDescription(row.Name);
        using var db = new FinoraDbContext();
        var transactions = db.Transactions
            .AsEnumerable()
            .Where(t => t.AmountCents < 0 &&
                string.Equals(NormalizeRecurringDescription(t.Description), normalizedName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        db.Transactions.RemoveRange(transactions);
        _ignoredSubscriptions.Add(normalizedName);
        UpsertSetting(db, IgnoredSubscriptionsSettingKey, System.Text.Json.JsonSerializer.Serialize(_ignoredSubscriptions.ToList()));
        db.SaveChanges();
        RecurringPayments.Remove(row);
        return transactions.Count;
    }

    private static RecurringPaymentRow? BuildRecurringPaymentRow(IReadOnlyList<Transaction> transactions)
    {
        var gaps = transactions
            .Zip(transactions.Skip(1), (previous, next) => (next.Date.Date - previous.Date.Date).TotalDays)
            .Where(days => days > 0)
            .OrderBy(days => days)
            .ToList();

        if (gaps.Count == 0)
        {
            return null;
        }

        var medianGap = gaps[gaps.Count / 2];
        var (frequency, days) = GetRecurringFrequency(medianGap);
        if (days == 0)
        {
            return null;
        }

        var last = transactions[^1];
        var amounts = transactions.Select(t => Math.Abs(t.AmountDollars)).ToList();
        var averageAmount = RoundDollars(amounts.Average());
        return new RecurringPaymentRow
        {
            AccountId = last.AccountId,
            Name = NormalizeRecurringDescription(last.Description),
            Amount = Math.Abs(last.AmountDollars),
            AverageAmount = averageAmount,
            MinAmount = amounts.Min(),
            MaxAmount = amounts.Max(),
            WeeklyAmount = GetWeeklyAmount(averageAmount, frequency),
            Frequency = frequency,
            AccountName = last.Account?.Name ?? "",
            LastPaid = last.Date.Date,
            NextExpected = last.Date.Date.AddDays(days),
            TimesSeen = transactions.Count,
            CategoryName = GetDisplayCategoryName(last.Category?.Name)
        };
    }

    public bool CreateBillFromRecurringPayment(RecurringPaymentRow recurringPayment)
    {
        using var db = new FinoraDbContext();
        if (!TryParseBillFrequency(recurringPayment.Frequency, out var frequency))
        {
            return false;
        }

        if (!db.Accounts.Any(a => a.Id == recurringPayment.AccountId))
        {
            return false;
        }

        var amountCents = (int)Math.Round(recurringPayment.Amount * 100m);
        var normalizedName = NormalizeRecurringDescription(recurringPayment.Name);
        var alreadyExists = db.Bills
            .AsEnumerable()
            .Any(b =>
                string.Equals(NormalizeRecurringDescription(b.Name), normalizedName, StringComparison.OrdinalIgnoreCase));

        if (alreadyExists)
        {
            return false;
        }

        db.Bills.Add(new Bill
        {
            Name = recurringPayment.Name,
            AccountId = recurringPayment.AccountId,
            AmountCents = amountCents,
            DueDate = recurringPayment.NextExpected.Date,
            NextPayDate = NextPayDate,
            Frequency = frequency,
            IsCreatedFromRecurringPayment = true
        });
        db.SaveChanges();

        AddBudgetItemKey(db, BuildBudgetItemKey("Bills", recurringPayment.Name));
        db.SaveChanges();
        return true;
    }

    private static bool TryParseBillFrequency(string frequency, out BillFrequency billFrequency)
    {
        return Enum.TryParse(frequency, ignoreCase: true, out billFrequency);
    }

    private static decimal GetWeeklyAmount(decimal amount, string frequency)
    {
        return frequency switch
        {
            "Weekly" => RoundDollars(amount),
            "Fortnightly" => RoundDollars(amount / 2m),
            "Monthly" => RoundDollars(amount * 12m / 52m),
            "Quarterly" => RoundDollars(amount * 4m / 52m),
            "Yearly" => RoundDollars(amount / 52m),
            _ => 0
        };
    }

    private static (string Frequency, int Days) GetRecurringFrequency(double medianGap)
    {
        return medianGap switch
        {
            >= 5 and <= 9 => ("Weekly", 7),
            >= 12 and <= 17 => ("Fortnightly", 14),
            >= 26 and <= 35 => ("Monthly", 30),
            >= 80 and <= 100 => ("Quarterly", 91),
            >= 350 and <= 380 => ("Yearly", 365),
            _ => ("", 0)
        };
    }

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
}
