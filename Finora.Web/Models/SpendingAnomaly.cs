namespace Finora.Web.Models;

public class SpendingAnomaly
{
    public string Merchant { get; set; } = string.Empty;
    public int TransactionId { get; set; }
    public decimal RecentAmount { get; set; }
    public decimal TypicalAmount { get; set; }
    public DateTime Date { get; set; }
}
