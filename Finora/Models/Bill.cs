namespace Finora.Models;

public enum BillFrequency
{
    Weekly,
    Fortnightly,
    Monthly,
    Quarterly,
    Yearly
}

public class Bill
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    public int? DebtId { get; set; }

    public Debt? Debt { get; set; }

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
}
