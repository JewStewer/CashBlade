namespace Finora.ViewModels;

public class TransactionSearchRow
{
    public string DateDisplay { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AmountDisplay => Amount < 0 ? $"-{Math.Abs(Amount):C}" : Amount.ToString("C");
    public string AmountColorHex => Amount < 0 ? "#F87171" : "#34D399";
}
