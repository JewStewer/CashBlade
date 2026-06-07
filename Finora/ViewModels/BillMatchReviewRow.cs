namespace Finora.ViewModels;

public class BillMatchReviewRow
{
    public int BillId { get; set; }

    public int TransactionId { get; set; }

    public string BillName { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public DateTime DueDate { get; set; }

    public string TransactionDescription { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; }

    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;
}
