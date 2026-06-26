namespace Finora.Web.Models;

// A real Stripe Issuing virtual card. Stripe is the source of truth for all
// of this — Evergrove only remembers the cardholder/card IDs (in settings)
// and re-fetches everything else live from the backend.
public class IssuedCard
{
    public string Id { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    public string Status { get; set; } = string.Empty;
    public int LimitCents { get; set; }
    public decimal LimitDollars => LimitCents / 100m;
}
