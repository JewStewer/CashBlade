namespace Finora.ViewModels;

public class DebtStrategyRow
{
    public string Strategy { get; set; } = string.Empty;

    public string FirstTarget { get; set; } = string.Empty;

    public string Order { get; set; } = string.Empty;

    public decimal ExtraPayment { get; set; }

    public int MonthsRemaining { get; set; }

    public decimal InterestPaid { get; set; }

    public decimal Principal { get; set; }

    public decimal MonthlyPaymentPool { get; set; }

    public int DebtCount { get; set; }

    public bool RollsOverMinimums { get; set; }

    public string InterestBreakdown { get; set; } = string.Empty;

    public string MonthsDisplay => MonthsRemaining <= 0 ? "n/a" : $"{MonthsRemaining} mo";

    public string EstimatedPaidOffDisplay => MonthsRemaining <= 0
        ? "n/a"
        : DateTime.Today.AddMonths(MonthsRemaining).ToString("MMM yyyy");

    public string InterestPaidDisplay => InterestPaid <= 0 ? "$0.00" : InterestPaid.ToString("C");

    public string PrincipalDisplay => Principal <= 0 ? "$0.00" : Principal.ToString("C");

    public string TotalPaidDisplay => Principal + InterestPaid <= 0 ? "$0.00" : (Principal + InterestPaid).ToString("C");

    public string MonthlyPaymentPoolDisplay => MonthlyPaymentPool <= 0 ? "$0.00/mo" : $"{MonthlyPaymentPool:C}/mo";

    public string InterestRateImpactDisplay => Principal <= 0 ? "n/a" : $"{InterestPaid / Principal:P0} of principal";

    public string DebtCountDisplay => DebtCount == 1 ? "1 debt" : $"{DebtCount} debts";

    public string RolloverDisplay => RollsOverMinimums ? "Minimums roll over" : "Minimums stay fixed";

    public string FirstTargetDisplay => string.IsNullOrWhiteSpace(FirstTarget) ? "No target" : FirstTarget;

    public string Summary => MonthsRemaining <= 0
        ? "Set payments to compare strategies."
        : $"{MonthsRemaining} months, about {InterestPaid:C} total interest across all debts";

    public string Detail => string.IsNullOrWhiteSpace(Order)
        ? "Add active debts to compare payoff order."
        : $"Order: {Order}";

    public string InterestDetail => string.IsNullOrWhiteSpace(InterestBreakdown)
        ? "No projected interest."
        : $"Interest from: {InterestBreakdown}";
}
