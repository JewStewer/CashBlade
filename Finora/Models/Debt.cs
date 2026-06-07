namespace Finora.Models;

public class Debt
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int BalanceCents { get; set; }

    public decimal BalanceDollars
    {
        get => BalanceCents / 100m;
        set => BalanceCents = (int)Math.Round(value * 100m);
    }

    public int MinimumPaymentCents { get; set; }

    public string PaymentPeriod { get; set; } = "Weekly";

    public decimal MinimumPaymentDollars
    {
        get => MinimumPaymentCents / 100m;
        set => MinimumPaymentCents = (int)Math.Round(value * 100m);
    }

    public decimal? InterestRate { get; set; }

    public int OriginalBalanceCents { get; set; }

    public decimal OriginalBalanceDollars
    {
        get => OriginalBalanceCents / 100m;
        set => OriginalBalanceCents = (int)Math.Round(value * 100m);
    }

    public string? UpPaymentMatchText { get; set; }
}
