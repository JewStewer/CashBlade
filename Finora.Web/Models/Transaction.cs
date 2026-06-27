namespace Finora.Web.Models;

public class Transaction
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public decimal AmountDollars
    {
        get => AmountCents / 100m;
        set => AmountCents = (int)Math.Round(value * 100m);
    }
    public int AccountId { get; set; }
    public int CategoryId { get; set; }
    public Guid? TransferId { get; set; }
    public string? UpTransactionId { get; set; }
    public bool IsUnnecessary { get; set; }
    // Money paid back to the user (e.g. a friend repaying them) — kept visible
    // as a normal transaction but excluded from income totals.
    public bool IsReimbursement { get; set; }
    // Precise Up Bank settlement/creation instant (Kind=Unspecified — never
    // serialised with an offset, so no timezone-shift risk). Null for
    // manually-entered transactions and any Up import predating this field.
    // Used only as a same-day sort tiebreaker, never for date grouping —
    // Date stays the deliberately time-stripped calendar day.
    public DateTime? UpSettledAt { get; set; }
    // Denormalised for display (filled from join in AppState)
    public string AccountName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
}
