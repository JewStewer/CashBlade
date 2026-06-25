namespace Finora.Web.Models;

public class PaydayTransferItem
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AccountColorHex { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
