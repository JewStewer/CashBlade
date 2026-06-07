namespace Finora.ViewModels;

public class SavedBudgetTileRow
{
    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Detail { get; set; } = string.Empty;

    public string ColorHex { get; set; } = "#CBD5E1";

    public string AmountDisplay => $"{Amount:C}/week";
}
