namespace Finora.Web.Models;

public class SavingsGoal
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TargetCents { get; set; }
    public decimal TargetDollars
    {
        get => TargetCents / 100m;
        set => TargetCents = (int)Math.Round(value * 100m);
    }
    public int CurrentCents { get; set; }
    public decimal CurrentDollars
    {
        get => CurrentCents / 100m;
        set => CurrentCents = (int)Math.Round(value * 100m);
    }
    public int WeeklyContributionCents { get; set; }
    public decimal WeeklyContributionDollars
    {
        get => WeeklyContributionCents / 100m;
        set => WeeklyContributionCents = (int)Math.Round(value * 100m);
    }
    public DateTime? TargetDate { get; set; }
    public string? GroupName { get; set; }

    // Set once, the first time a TargetDate is assigned, so pace can be measured
    // against where the goal started rather than its current (already-progressed) balance.
    public DateTime? TargetStartDate { get; set; }
    public int? TargetStartingBalanceCents { get; set; }
}
