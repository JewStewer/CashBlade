namespace Finora.ViewModels;

public class CoverTransferGroupRow
{
    public DateTime Date { get; set; }

    public string FromAccount { get; set; } = string.Empty;

    public string ToAccount { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Display => $"{FromAccount} -> {ToAccount}";
}
