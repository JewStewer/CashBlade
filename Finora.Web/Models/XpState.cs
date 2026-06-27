namespace Finora.Web.Models;

public class XpState
{
    public int TotalXp { get; set; }
    public DateTime? LastDailyLoginAward { get; set; }
    public DateTime? LastEvaluatedWeekStart { get; set; }
    public List<int> AwardedBillStatusIds { get; set; } = new();
    public int Level => 1 + TotalXp / 100;
    public int XpIntoLevel => TotalXp % 100;
}
