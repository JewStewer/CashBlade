namespace Finora.ViewModels;

public class BudgetVarianceRow
{
    public string Category { get; set; } = string.Empty;

    public decimal Budgeted { get; set; }

    public decimal Actual { get; set; }

    public bool HasBudget => Budgeted > 0;

    public decimal Difference => Budgeted - Actual;

    public string Status => Difference >= 0 ? "Under" : "Over";

    public string ColorHex => HasBudget
        ? (Difference >= 0 ? "#34D399" : "#F87171")
        : "#6E7681";

    public decimal PercentUsed => Budgeted <= 0 ? 0 : Math.Clamp(Actual / Budgeted, 0, 1);

    public string PercentUsedLabel => Budgeted <= 0 ? "-" : $"{PercentUsed * 100:0}% used";
}

