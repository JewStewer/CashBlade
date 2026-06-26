namespace Finora.Web.Models;

// A logged entry against a spending limit (PrepaidCard). Phone-only — never synced
// as a real Transaction, so it can't double-count once the real purchase lands
// from Up. Positive amount = limit raised, negative = spend logged.
public class CardActivity
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public decimal AmountDollars => AmountCents / 100m;
    public DateTime Date { get; set; }
}
