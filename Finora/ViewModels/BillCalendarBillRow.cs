namespace Finora.ViewModels;

public class BillCalendarBillRow
{
    public int BillId { get; set; }

    public DateTime DueDate { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public bool IsPaid { get; set; }

    public string Display => $"{Name} {Amount:C}";
    public string PaidStatusLabel => IsPaid ? "Paid" : "Due";
    public string StatusColorHex => IsPaid ? "#34D399" : "#F59E0B";
    public string CardBackground => IsPaid ? "#0D2A1E" : "#1F1200";
}
