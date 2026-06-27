namespace Finora.Web.Models;

public static class GoalMath
{
    // Mirrors WPF's SavingsGoalWindow.CalculateWeeklyRequired so the
    // recommendation updates live as target/current/date are edited.
    public static decimal WeeklyRequired(decimal target, decimal current, DateTime? targetDate)
    {
        if (targetDate is null) return 0;
        var remaining = Math.Max(target - current, 0);
        if (remaining <= 0) return 0;
        var days = Math.Max((targetDate.Value.Date - DateTime.Today).TotalDays, 1);
        var weeks = Math.Max((decimal)days / 7m, 1m);
        return Math.Ceiling((remaining / weeks) * 100m) / 100m;
    }

    public enum PaceStatus { AheadOfPace, OnPace, BehindPace, NoTarget }

    /// <summary>Compares elapsed time vs elapsed progress toward a dated savings target.</summary>
    public static PaceStatus GetPaceStatus(decimal target, decimal current, decimal startingBalance, DateTime? startDate, DateTime? targetDate)
    {
        if (targetDate is null || startDate is null || target <= startingBalance) return PaceStatus.NoTarget;

        var totalDays = (targetDate.Value.Date - startDate.Value.Date).TotalDays;
        if (totalDays <= 0) return PaceStatus.NoTarget;

        var elapsedDays = (DateTime.Today - startDate.Value.Date).TotalDays;
        var expectedProgress = Math.Clamp(elapsedDays / totalDays, 0, 1);
        var actualProgress = Math.Clamp((double)((current - startingBalance) / (target - startingBalance)), 0, 1);

        var delta = actualProgress - expectedProgress;
        if (delta > 0.05) return PaceStatus.AheadOfPace;
        if (delta < -0.05) return PaceStatus.BehindPace;
        return PaceStatus.OnPace;
    }

    /// <summary>Weeks until a target is reached at a given weekly contribution. -1 if it would never be reached.</summary>
    public static int WeeksToTarget(decimal target, decimal current, decimal weeklyContribution)
    {
        var remaining = target - current;
        if (remaining <= 0) return 0;
        if (weeklyContribution <= 0) return -1;
        return (int)Math.Ceiling(remaining / weeklyContribution);
    }
}
