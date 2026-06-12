namespace Finora.ViewModels;

public class AffordabilityWeekRow
{
    public int WeekNumber { get; set; }
    public decimal StartBalance { get; set; }
    public decimal TopUpAmount { get; set; }
    public decimal BillsAmount { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal LeftAfterPurchase { get; set; }
    public decimal EndBalance { get; set; }

    public string WeekLabel => $"Wk {WeekNumber}";
    public string StartBalanceDisplay => $"{StartBalance:C}";
    public string BillsDisplay => BillsAmount <= 0 ? "—" : $"-{BillsAmount:C}";
    public string PurchaseDisplay => PurchaseAmount <= 0 ? "—" : $"-{PurchaseAmount:C}";
    public string LeftAfterPurchaseDisplay => $"{LeftAfterPurchase:C}";
    public string LeftAfterPurchaseColorHex => LeftAfterPurchase < 0 ? "#F87171" : LeftAfterPurchase < 30 ? "#F59E0B" : "#6EE7B7";
    public string EndBalanceDisplay => $"{EndBalance:C}";
    public string EndColorHex => EndBalance < 0 ? "#F87171" : EndBalance < 30 ? "#F59E0B" : "#6EE7B7";
    public bool IsNegative => EndBalance < 0;
}
