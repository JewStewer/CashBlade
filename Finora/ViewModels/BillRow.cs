namespace Finora.ViewModels;

public class BillRow : ViewModelBase
{
    private bool _isPaid;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime DueDate { get; set; }

    public DateTime NextPayDate { get; set; }

    public string Frequency { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public string PeriodType { get; set; } = string.Empty;

    public decimal AccountBalance { get; set; }

    public decimal NeededNow { get; set; }

    public bool IsAutoPay { get; set; }

    public string MatchNote { get; set; } = string.Empty;

    public bool IsPaid
    {
        get => _isPaid;
        set
        {
            if (SetProperty(ref _isPaid, value))
            {
                OnPropertyChanged(nameof(CoverageStatus));
                OnPropertyChanged(nameof(BalanceAfterBill));
            }
        }
    }

    public decimal BalanceAfterBill => IsPaid ? AccountBalance : AccountBalance - Amount;

    public string CoverageStatus => IsPaid
        ? "Paid"
        : NeededNow <= 0
            ? "Covered"
            : $"Needs {NeededNow:C}";

    public string DangerBadge
    {
        get
        {
            if (IsPaid)
            {
                return "Paid";
            }

            if (NeededNow <= 0)
            {
                return DueDate.Date <= NextPayDate.Date ? "Ready" : "";
            }

            return DueDate.Date <= NextPayDate.Date ? "Danger" : "Short";
        }
    }

    public string PayTiming
    {
        get
        {
            if (DueDate.Date >= PeriodStart.Date && DueDate.Date <= PeriodEnd.Date)
            {
                return PeriodType == "Weekly" ? "This week" : "This month";
            }

            return DueDate.Date <= NextPayDate.Date ? "Before payday" : "After payday";
        }
    }

    public string StatusColorHex => IsPaid
        ? "#34D399"
        : NeededNow <= 0
            ? "#0D9488"
            : DueDate.Date <= NextPayDate.Date
                ? "#F87171"
                : "#F59E0B";
}
