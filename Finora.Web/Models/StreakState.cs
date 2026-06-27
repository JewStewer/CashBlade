namespace Finora.Web.Models;

public class StreakState
{
    public int CurrentStreakWeeks { get; set; }
    public int BestStreakWeeks { get; set; }
    public DateTime? LastEvaluatedWeekStart { get; set; }
    public int FreezesAvailable { get; set; }
    public DateTime? LastFreezeUsedWeekStart { get; set; }
}
