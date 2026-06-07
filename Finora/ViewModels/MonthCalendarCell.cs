namespace Finora.ViewModels;

public class MonthCalendarCell
{
    public bool IsPadding { get; set; }
    public int Day { get; set; }
    public string DayText => IsPadding ? "" : Day.ToString();
    public string Grade { get; set; } = "";
    public string GradeColorHex { get; set; } = "#475569";
    public string BackgroundHex { get; set; } = "#0D1117";
    public string BorderHex { get; set; } = "#1C2433";
    public string SpendingText { get; set; } = "";
    public bool HasSpending { get; set; }
}
