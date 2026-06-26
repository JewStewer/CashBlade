namespace Finora.Web.Models;

// A real Stripe Issuing authorization attempt — includes declines, unlike
// Stripe's Transactions which only ever represent money that actually moved.
public class CardActivityEntry
{
    public string Id { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public decimal AmountDollars => AmountCents / 100m;
    public bool Approved { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
