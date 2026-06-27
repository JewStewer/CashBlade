namespace Finora.Web.Models;

public class RoundUpState
{
    public bool Enabled { get; set; }
    public int RoundToCents { get; set; } = 100;
    public int AccumulatedCents { get; set; }
    public int LastProcessedTransactionId { get; set; }
    public decimal AccumulatedDollars => AccumulatedCents / 100m;
}
