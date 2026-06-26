namespace Finora.Web.Models;

// A virtual, phone-only prepaid card: load money onto it, then spend from it.
// Balance can never go negative — that hard cap is the whole point.
public class PrepaidCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#14B8A6";
    public int BalanceCents { get; set; }
    public decimal BalanceDollars
    {
        get => BalanceCents / 100m;
        set => BalanceCents = (int)Math.Round(value * 100m);
    }
}
