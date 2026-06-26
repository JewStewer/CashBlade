namespace Finora.Web.Models;

// A card label over a real Account — typically a dedicated Up Saver with its own
// virtual card/Apple Pay. Balance and activity always come straight from that real,
// synced Account, so the actual decline at the register is enforced by the bank's
// own insufficient-funds check, not by this app — there is nothing here that can
// drift from real money.
public class PrepaidCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#14B8A6";
    public int AccountId { get; set; }
}
