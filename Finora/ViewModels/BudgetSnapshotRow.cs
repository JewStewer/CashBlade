namespace Finora.ViewModels;

public class BudgetSnapshotRow
{
    public DateTime CreatedAt { get; set; }

    public decimal Income { get; set; }

    public decimal Bills { get; set; }

    public decimal Essentials { get; set; }

    public decimal Savings { get; set; }

    public decimal Unplanned { get; set; }

    public decimal Total => Bills + Essentials + Savings + Unplanned;

    public string Summary => $"Income {Income:C}, budget {Total:C}, left {Income - Total:C}";
}
