namespace Finora.Web.Models;

public class SavingsGoalGroup
{
    public string Name { get; set; } = string.Empty;
    public List<SavingsGoal> Goals { get; set; } = new();

    public decimal TotalCurrentDollars => Goals.Sum(g => g.CurrentDollars);
    public decimal TotalTargetDollars => Goals.Sum(g => g.TargetDollars);
    public decimal Progress => TotalTargetDollars > 0 ? Math.Clamp(TotalCurrentDollars / TotalTargetDollars, 0, 1) : 0;
}
