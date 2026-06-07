namespace Finora.ViewModels;

public class WeekCalendarDay
{
    public string DayAbbrev { get; set; } = string.Empty;   // Mon, Tue …
    public string SpendingDisplay { get; set; } = string.Empty;
    public string Grade { get; set; } = "—";
    public string GradeColorHex { get; set; } = "#334155";
    public string BackgroundHex { get; set; } = "#0B1120";
    public string BorderHex { get; set; } = "#243244";
    public bool IsToday { get; set; }
    public bool IsFuture { get; set; }
}
