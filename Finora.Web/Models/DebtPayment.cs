namespace Finora.Web.Models;

public class DebtPayment
{
    public int Id { get; set; }
    public int DebtId { get; set; }
    public string UpTransactionId { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public decimal AmountDollars
    {
        get => AmountCents / 100m;
        set => AmountCents = (int)Math.Round(value * 100m);
    }
    public DateTime PaidOn { get; set; }
    public string Description { get; set; } = string.Empty;
}
