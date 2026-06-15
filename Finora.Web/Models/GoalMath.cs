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
}
