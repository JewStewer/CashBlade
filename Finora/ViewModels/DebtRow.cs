namespace Finora.ViewModels;

public class DebtRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public decimal MinimumPayment { get; set; }

    public string PaymentPeriod { get; set; } = "Weekly";

    public string PaymentDisplay => $"{MinimumPayment:C} {PaymentPeriod.ToLowerInvariant()}";

    public decimal? InterestRate { get; set; }

    public decimal OriginalBalance { get; set; }

    public string UpPaymentMatchText { get; set; } = string.Empty;

    public decimal RecordedPaid { get; set; }

    public decimal PaidOff => Math.Max(OriginalBalance - Balance, RecordedPaid);

    public decimal PayoffProgress
    {
        get
        {
            var startingBalance = OriginalBalance > 0 ? OriginalBalance : Balance + PaidOff;
            return startingBalance <= 0 ? 0 : Math.Clamp(PaidOff / startingBalance, 0, 1);
        }
    }

    public string PayoffProgressDisplay => PayoffProgress.ToString("P0");

    public string InterestRateDisplay => InterestRate is null ? "" : $"{InterestRate:0.##}%";

    public bool IncludeInStrategy { get; set; } = true;
}
