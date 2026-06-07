namespace Finora.ViewModels;

public class DailyTrackerTransactionRow : ViewModelBase
{
    private bool _isUnnecessary;

    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ResolvedDisplayName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public bool IsBillPayment { get; set; }
    public string BillName { get; set; } = string.Empty;

    public bool IsSpending => Amount < 0;
    public bool IsIncome => Amount > 0;
    public string AmountDisplay => Amount < 0 ? $"-{Math.Abs(Amount):C}" : $"+{Amount:C}";
    public string SpendingAmountDisplay => IsIncome ? $"+{Amount:C}" : Math.Abs(Amount).ToString("C");

    public bool IsUnnecessary
    {
        get => _isUnnecessary;
        set
        {
            if (SetProperty(ref _isUnnecessary, value))
            {
                OnPropertyChanged(nameof(NecessaryLabel));
                OnPropertyChanged(nameof(NecessaryColor));
                OnPropertyChanged(nameof(NecessaryDot));
                OnPropertyChanged(nameof(LeftBorderColorHex));
                OnPropertyChanged(nameof(AmountColorHex));
            }
        }
    }

    public string NecessaryLabel => IsUnnecessary ? "Mark as needed" : "Mark as unnecessary";
    public string NecessaryColor => IsUnnecessary ? "#F87171" : "#6E7681";
    public string NecessaryDot => IsUnnecessary ? "✕" : "○";
    public string LeftBorderColorHex => IsIncome ? "#3FB950" : (IsUnnecessary ? "#F87171" : "#21262D");
    public string AmountColorHex => IsIncome ? "#3FB950" : (IsUnnecessary ? "#F87171" : "#8B949E");

    public string DisplayDescription
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ResolvedDisplayName))
                return ResolvedDisplayName;
            return System.Text.RegularExpressions.Regex.Replace(Description, @"\s+\d{7,}\s*$", "").Trim();
        }
    }
}
