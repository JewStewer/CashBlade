namespace Finora.ViewModels;

public class BillDueSoonRow
{
    public string Name { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public DateTime DueDate { get; set; }

    public decimal Amount { get; set; }

    public int DaysUntil { get; set; }

    public decimal AccountBalance { get; set; }

    public string DueSummary => DaysUntil == 0 ? "today" : DaysUntil == 1 ? "tomorrow" : $"in {DaysUntil} days";

    public string ColorHex
    {
        get
        {
            if (AccountBalance >= Amount) return "#34D399";
            if (AccountBalance > 0) return "#F59E0B";
            return "#F87171";
        }
    }

    public string DisplayLine => $"{Name} — {Amount:C} due {DueSummary}";
}
