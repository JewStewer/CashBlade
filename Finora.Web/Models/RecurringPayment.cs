namespace Finora.Web.Models;

public class RecurringPayment
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AverageAmount { get; set; }
    public decimal MinAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public decimal WeeklyAmount { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime LastPaid { get; set; }
    public DateTime NextExpected { get; set; }
    public int TimesSeen { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsAlreadyBill { get; set; }

    public int DaysUntilNext => (NextExpected.Date - DateTime.Today).Days;

    public string NextDueDisplay => DaysUntilNext switch
    {
        < 0 => $"Overdue by {Math.Abs(DaysUntilNext)} days",
        0 => "Today",
        1 => "Tomorrow",
        _ => $"In {DaysUntilNext} days"
    };

    public string AmountRangeDisplay => MinAmount == MaxAmount
        ? Amount.ToString("C")
        : $"{MinAmount:C} - {MaxAmount:C}";
}
