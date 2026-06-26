namespace Finora.Web.Models;

// Top-up / spend ledger entry for a PrepaidCard. Positive amount = top up, negative = spend.
public class CardActivity
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public decimal AmountDollars
    {
        get => AmountCents / 100m;
        set => AmountCents = (int)Math.Round(value * 100m);
    }
    public DateTime Date { get; set; } = DateTime.Today;
}
