namespace Finora.Web.Models;

public class MerchantSpendWatchItem
{
    public string Merchant { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal UnnecessaryAmount { get; set; }
    public int Count { get; set; }
    public DateTime LastSeen { get; set; }
}
