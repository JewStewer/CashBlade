namespace Finora.ViewModels;

public class DebtPayoffPlanRow
{
    public int DebtId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public decimal MinimumPayment { get; set; }

    public decimal? InterestRate { get; set; }

    public string PaymentPeriod { get; set; } = string.Empty;

    public string PaymentDisplay => string.IsNullOrWhiteSpace(PaymentPeriod)
        ? MinimumPayment.ToString("C")
        : $"{MinimumPayment:C} {PaymentPeriod.ToLowerInvariant()}";

    public int MonthsRemaining { get; set; }

    public bool PaymentCoversInterest { get; set; } = true;

    public string RateDisplay => InterestRate is null ? "No interest" : $"{InterestRate:0.##}% APR";

    public string MonthsRemainingDisplay => !PaymentCoversInterest
        ? "No payoff"
        : MonthsRemaining <= 0
            ? "Set payment"
            : MonthsRemaining.ToString();

    public DateTime? EstimatedPaidOff { get; set; }

    public string EstimatedPaidOffDisplay => !PaymentCoversInterest
        ? "Payment too low"
        : EstimatedPaidOff is null
        ? "Set payment"
        : $"{EstimatedPaidOff:MMM yyyy}";
}
