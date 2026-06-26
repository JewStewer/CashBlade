namespace Finora.Web.Models;

// A self-imposed spending limit, tracked only on this phone. Raising the limit or
// logging a spend never moves real money and never creates a real Transaction —
// it's purely a discipline tool to cover what Up's own accounts don't offer.
public class PrepaidCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#14B8A6";
    public int BalanceCents { get; set; }
    public decimal BalanceDollars => BalanceCents / 100m;
}
