namespace Finora.Models;

public class BillOccurrenceStatus
{
    public int Id { get; set; }

    public int BillId { get; set; }

    public Bill? Bill { get; set; }

    public DateTime DueDate { get; set; }

    public bool IsPaid { get; set; }

    public bool IsSkipped { get; set; }

    public int? MatchedTransactionId { get; set; }

    public string MatchNote { get; set; } = string.Empty;

    public DateTime? PaidOn { get; set; }

    public string? OriginalTransactionDescription { get; set; }

    public int? OriginalTransactionCategoryId { get; set; }

    public string? OriginalTransactionTransferId { get; set; }
}
