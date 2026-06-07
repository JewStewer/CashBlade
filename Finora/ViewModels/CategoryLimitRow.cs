namespace Finora.ViewModels;

public class CategoryLimitRow
{
    public string Category { get; set; } = string.Empty;
    public decimal WeeklyLimit { get; set; }
    public string WeeklyLimitDisplay => WeeklyLimit.ToString("C");
}
