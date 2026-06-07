namespace Finora.ViewModels;

public class SavingsGoalRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Target { get; set; }

    public decimal Current { get; set; }

    public decimal WeeklyContribution { get; set; }

    public DateTime? TargetDate { get; set; }

    public string TargetDateDisplay => TargetDate?.ToString("dd/MM/yyyy") ?? "";

    public string WeeklyContributionDisplay => WeeklyContribution.ToString("C");

    public decimal Progress => Target <= 0 ? 0 : Math.Clamp(Current / Target, 0, 1);

    public string EstimatedTimeToGoal
    {
        get
        {
            var remaining = Target - Current;
            if (remaining <= 0)
            {
                return "Complete";
            }

            if (WeeklyContribution <= 0)
            {
                return "Set weekly amount";
            }

            var weeks = (int)Math.Ceiling(remaining / WeeklyContribution);
            return weeks == 1 ? "1 week" : $"{weeks} weeks";
        }
    }
}
