namespace Finora.ViewModels;

public class DebtPaymentAuditRow
{
    public DateTime PaidOn { get; set; }

    public string DebtName { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;
}
