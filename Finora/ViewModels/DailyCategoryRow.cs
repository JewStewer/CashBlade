namespace Finora.ViewModels;

public class DailyCategoryRow
{
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal WeeklyLimit { get; set; }

    public string TotalDisplay => Total.ToString("C");
    public bool HasLimit => WeeklyLimit > 0;
    public decimal WeeklyAverage => Total / 2m; // 14-day window
    public bool IsOverLimit => HasLimit && WeeklyAverage > WeeklyLimit;
    public bool IsNearLimit => HasLimit && !IsOverLimit && WeeklyAverage >= WeeklyLimit * 0.8m;
    public string ChipColorHex => IsOverLimit ? "#F87171" : IsNearLimit ? "#FBBF24" : "#94A3B8";
    public string LimitSuffix => HasLimit ? $"  /  {WeeklyLimit:C}/wk" : string.Empty;
}
