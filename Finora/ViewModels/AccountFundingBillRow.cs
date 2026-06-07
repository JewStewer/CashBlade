namespace Finora.ViewModels;

public class AccountFundingBillRow
{
    public string Name { get; set; } = string.Empty;

    public DateTime DueDate { get; set; }

    public decimal Amount { get; set; }

    public decimal NeededNow { get; set; }

    public decimal RemainingAfterBill { get; set; }
}
