namespace Finora.Web.Models;

public record DailyScore(
    DateTime Date,
    decimal SpendingTotal,
    decimal UnnecessaryTotal,
    int Score,
    string Grade,
    string ColorHex)
{
    public decimal NecessaryTotal => SpendingTotal - UnnecessaryTotal;
    public bool HasSpending => SpendingTotal > 0;
    public bool IsToday => Date.Date == DateTime.Today;
}
