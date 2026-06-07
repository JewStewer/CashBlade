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
    // Denormalised for display (filled from join in AppState)
    public string AccountName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
}
