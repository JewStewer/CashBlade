namespace Finora.ViewModels;

public class FinancialHealthComponent
{
    public string Name { get; set; } = "";

    public int Score { get; set; }

    public int MaxScore { get; set; } = 25;

    /// <summary>Current-state description, e.g. "3.2 weeks covered".</summary>
    public string Detail { get; set; } = "";

    /// <summary>Actionable tip shown when not at full score. Empty when maxed.</summary>
    public string Tip { get; set; } = "";

    public string ColorHex { get; set; } = "#34D399";

    public decimal Progress => MaxScore <= 0 ? 0 : Math.Clamp((decimal)Score / MaxScore, 0, 1);

    public bool HasTip => !string.IsNullOrEmpty(Tip);
}
