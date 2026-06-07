namespace Finora.ViewModels;

public class AccountRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public string ColorHex { get; set; } = "#0F766E";

    public decimal NeededNow { get; set; }

    public decimal BillsDue { get; set; }

    public decimal RemainingAfterBills => Balance - BillsDue;

    public string RemainingAfterBillsColorHex => RemainingAfterBills >= 0 ? "#34D399" : "#F87171";

    public string RemainingAfterBillsSummary => BillsDue <= 0
        ? string.Empty
        : RemainingAfterBills >= 0
            ? $"After bills: {RemainingAfterBills:C}"
            : $"Short {-RemainingAfterBills:C} after bills";

    public string BillsDueDisplay => BillsDue <= 0 ? string.Empty : $"Bills {BillsDue:C} due";

    public string BillsCoverageDisplay => BillsDue <= 0 ? string.Empty
        : RemainingAfterBills >= 0 ? $"{RemainingAfterBills:C} left"
        : $"Short {-RemainingAfterBills:C}";

    public decimal? Target { get; set; }

    public DateTime? TargetDate { get; set; }

    public DateTime? TargetStartDate { get; set; }

    public decimal? TargetStartingBalance { get; set; }

    public DateTime NextPayDate { get; set; } = DateTime.Today;

    public decimal TargetRemaining => Target is null ? 0 : Math.Max(Target.Value - Balance, 0);

    public decimal TargetProgress => Target is null || Target <= 0 ? 0 : Math.Clamp(Balance / Target.Value, 0, 1);

    public decimal TargetExpectedBalance
    {
        get
        {
            if (Target is null || TargetDate is null)
            {
                return 0;
            }

            var startDate = (TargetStartDate ?? DateTime.Today).Date;
            var targetDate = TargetDate.Value.Date;
            var startingBalance = TargetStartingBalance ?? Balance;

            if (targetDate <= startDate)
            {
                return Target.Value;
            }

            var elapsedDays = Math.Clamp((DateTime.Today - startDate).TotalDays, 0, (targetDate - startDate).TotalDays);
            var totalDays = (decimal)(targetDate - startDate).TotalDays;
            var progress = (decimal)elapsedDays / totalDays;
            return Math.Round(startingBalance + ((Target.Value - startingBalance) * progress), 2);
        }
    }

    public decimal TargetBehindAmount => Target is null || TargetDate is null
        ? 0
        : Math.Max(TargetExpectedBalance - Balance, 0);

    public string NeededNowStatus => NeededNow > 0 ? "Needs funding" : "Covered";

    public string NeededNowColorHex => NeededNow > 0 ? "#F59E0B" : "#34D399";

    // ── Next upcoming bill for this account ────────────────────────────────────
    public string? NextUpcomingBillName { get; set; }
    public decimal  NextUpcomingBillAmount { get; set; }
    public DateTime? NextUpcomingBillDate { get; set; }

    /// <summary>
    /// Single-line label shown on the account card, e.g. "Netflix  $14.99  ·  Tue".
    /// Empty string when there is no upcoming unpaid bill before payday.
    /// </summary>
    public string NextUpcomingBillDisplay
    {
        get
        {
            if (NextUpcomingBillDate is null || string.IsNullOrEmpty(NextUpcomingBillName))
                return string.Empty;

            var date  = NextUpcomingBillDate.Value.Date;
            var today = DateTime.Today;
            var dateStr = date == today            ? "today"
                        : date == today.AddDays(1) ? "tomorrow"
                        : date <= today.AddDays(6) ? date.ToString("ddd")   // "Mon"
                        : date.ToString("d MMM");                            // "15 Jun"

            return $"{NextUpcomingBillName}  {NextUpcomingBillAmount:C}  ·  {dateStr}";
        }
    }

    // ── Target display helpers ──────────────────────────────────────────────
    public string TargetAmountDisplay => Target is null ? "Not set" : $"{Target.Value:C}";

    /// <summary>"of $440.00" appended next to the progress percentage, empty when no target.</summary>
    public string TargetAmountLabel => Target is null ? string.Empty : $"of {Target.Value:C}";

    public string TargetDateDisplay => TargetDate is null ? "" : $"by {TargetDate.Value:dd/MM/yyyy}";

    public string TargetPaceColorHex
    {
        get
        {
            if (Target is null)
            {
                return "#64748B";
            }

            if (TargetRemaining <= 0 || TargetBehindAmount < 0.01m)
            {
                return "#34D399";
            }

            return "#F59E0B";
        }
    }

    public string TargetPaceSummary
    {
        get
        {
            if (Target is null)
            {
                return string.Empty;
            }

            if (TargetRemaining <= 0)
            {
                return "Ahead: target reached";
            }

            if (TargetDate is null)
            {
                return "Add a target date to see if you are behind";
            }

            if (TargetDate.Value.Date < DateTime.Today)
            {
                return $"Target date passed | Short {TargetRemaining:C}";
            }

            if (TargetBehindAmount >= 0.01m)
            {
                return $"Behind {TargetBehindAmount:C} | Should be {TargetExpectedBalance:C}";
            }

            var aheadBy = Math.Max(Balance - TargetExpectedBalance, 0);
            return aheadBy >= 0.01m
                ? $"Ahead {aheadBy:C} | Should be {TargetExpectedBalance:C}"
                : $"On track | Should be {TargetExpectedBalance:C}";
        }
    }

    public decimal PayPeriodTargetContribution
    {
        get
        {
            if (Target is null || TargetRemaining <= 0 || TargetDate is null)
            {
                return 0;
            }

            var payPeriodsRemaining = CountPayPeriodsBeforeTargetDate();
            return Math.Ceiling((TargetRemaining / payPeriodsRemaining) * 100m) / 100m;
        }
    }

    public string PayPeriodTargetContributionSummary
    {
        get
        {
            if (Target is null)
            {
                return string.Empty;
            }

            if (TargetRemaining <= 0)
            {
                return "Target reached";
            }

            if (TargetDate is null)
            {
                return "Set a target date for weekly amount";
            }

            return $"Put in {PayPeriodTargetContribution:C}/pay";
        }
    }

    public string WeeklyTargetContributionSummary => PayPeriodTargetContributionSummary;

    public int WeeksToTarget => Target is null || TargetRemaining <= 0 ? 0 : CountPayPeriodsBeforeTargetDate();

    public string TargetCountdownSummary
    {
        get
        {
            if (Target is null || TargetRemaining <= 0) return string.Empty;
            if (TargetDate is null) return $"{TargetRemaining:C} to go";
            var weeks = WeeksToTarget;
            return weeks <= 0
                ? $"Due now — {TargetRemaining:C} short"
                : $"{weeks} wk{(weeks == 1 ? "" : "s")} to target | {PayPeriodTargetContribution:C}/wk";
        }
    }

    public string TargetSummary => Target is null
        ? "No target set"
            : TargetDate is null
                ? $"Target: {Target.Value:C} | Need {TargetRemaining:C}"
                : $"Target: {Target.Value:C} by {TargetDate.Value:dd/MM/yyyy} | Need {TargetRemaining:C}";

    private int CountPayPeriodsBeforeTargetDate()
    {
        if (TargetDate is null)
        {
            return 1;
        }

        var payDate = NextPayDate.Date;
        while (payDate < DateTime.Today)
        {
            payDate = payDate.AddDays(7);
        }

        var count = 0;
        while (payDate < TargetDate.Value.Date)
        {
            count++;
            payDate = payDate.AddDays(7);
        }

        return Math.Max(count, 1);
    }
}
