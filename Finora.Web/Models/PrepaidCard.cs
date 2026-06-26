namespace Finora.Web.Models;

// A visual "card" label over a real Account (typically an Up Saver with its own
// real Mastercard/Apple Pay support). Balance and activity always come straight
// from that real, synced Account — this is purely a display layer, never a
// separate ledger, so there is nothing here that can drift from real money.
public class PrepaidCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#14B8A6";
    public int AccountId { get; set; }
}
