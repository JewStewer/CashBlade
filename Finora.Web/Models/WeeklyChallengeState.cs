namespace Finora.Web.Models;

public class WeeklyChallengeState
{
    public DateTime WeekStart { get; set; }
    public string ChallengeKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public string? CategoryName { get; set; }
    public bool? Passed { get; set; }
    public int XpReward { get; set; }
    public bool XpAwarded { get; set; }
}
