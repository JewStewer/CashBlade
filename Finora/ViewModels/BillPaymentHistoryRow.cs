namespace Finora.ViewModels;

public class BillPaymentHistoryRow
{
    public string BillName { get; set; } = string.Empty;

    public DateTime DueDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public string PaidOnDisplay { get; set; } = string.Empty;

    public string MatchedTransaction { get; set; } = string.Empty;

    public string MatchNote { get; set; } = string.Empty;
}
