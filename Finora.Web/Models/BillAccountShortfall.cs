namespace Finora.Web.Models;

public class BillAccountShortfall
{
    public string AccountName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public decimal DueBeforePayday { get; set; }
    public int BillCount { get; set; }
    public decimal Needed => Math.Max(DueBeforePayday - CurrentBalance, 0);
}
