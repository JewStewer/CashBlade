namespace Finora.Web.Models;

public enum BillFrequency { Weekly, Fortnightly, Monthly, Quarterly, Yearly }

public class Bill
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public int? DebtId { get; set; }
    // Total purchase price for installment bills (e.g. $89.12 for 4×$22.28).
    // Stored directly so it survives sync even when DebtId can't be reconciled
    // server-side. Zero means "not an installment bill."
    public int TotalInstallmentAmountCents { get; set; }
    public decimal TotalInstallmentAmountDollars
    {
        get => TotalInstallmentAmountCents / 100m;
        set => TotalInstallmentAmountCents = (int)Math.Round(value * 100m);
    }
    public int AmountCents { get; set; }
    public decimal AmountDollars
    {
        get => AmountCents / 100m;
        set => AmountCents = (int)Math.Round(value * 100m);
    }
    public DateTime DueDate { get; set; } = DateTime.Today;
    public DateTime NextPayDate { get; set; } = DateTime.Today;
    public BillFrequency Frequency { get; set; } = BillFrequency.Monthly;
    public bool IsPaid { get; set; }
    public bool IsCreatedFromRecurringPayment { get; set; }
    public bool IsAutoPay { get; set; }
    public string PaymentMatchText { get; set; } = string.Empty;
    // Denormalised
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Current-cycle due date, computed by AppState.DenormaliseBills().
    /// Advances bill.DueDate by the bill's frequency until it reaches the current
    /// billing period — needed when Up Bank auto-matching sets IsPaid on a status
    /// record but does not advance the stored DueDate.
    /// Falls back to DueDate before AppState has run.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime EffectiveDueDate { get; set; }
}
