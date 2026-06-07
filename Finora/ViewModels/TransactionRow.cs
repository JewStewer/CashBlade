namespace Finora.ViewModels;

public class TransactionRow
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public string CoverPair { get; set; } = string.Empty;

    public Guid? TransferId { get; set; }

    public bool IsTransfer => TransferId is { } transferId && transferId != Guid.Empty;
}
