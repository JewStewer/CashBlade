using System.Collections.ObjectModel;

namespace Finora.ViewModels;

public class DailyTrackerDayRow
{
    public DateTime Date { get; set; }
    public ObservableCollection<DailyTrackerTransactionRow> Transactions { get; } = new();
    public ObservableCollection<BillCalendarBillRow> Bills { get; } = new();

    public bool IsToday => Date.Date == DateTime.Today;
    public string DayLabel => IsToday ? "Today" : Date.ToString("dddd");
    public string DateLabel => Date.ToString("d MMM yyyy");

    public decimal SpendingTotal => Math.Abs(Transactions.Where(t => t.IsSpending).Sum(t => t.Amount));
    public decimal UnnecessaryTotal => Math.Abs(Transactions.Where(t => t.IsSpending && t.IsUnnecessary).Sum(t => t.Amount));
    public decimal NecessaryTotal => SpendingTotal - UnnecessaryTotal;

    public bool HasUnnecessarySpending => UnnecessaryTotal > 0;
    public bool IsCleanDay => !HasUnnecessarySpending && SpendingTotal > 0;
    public bool HasAnyActivity => Transactions.Count > 0 || Bills.Count > 0;
    public bool HasSpending => SpendingTotal > 0;

    public string SpendingDisplay => SpendingTotal > 0 ? SpendingTotal.ToString("C") : "No spending";
    public string UnnecessaryDisplay => UnnecessaryTotal > 0 ? UnnecessaryTotal.ToString("C") : "None";
    public string CleanBadge => !HasUnnecessarySpending && SpendingTotal > 0 ? "✓ Clean" : "";

    public int DayScore => SpendingTotal == 0 ? 100 : (int)(NecessaryTotal / SpendingTotal * 100);
    public string DayGrade => !HasSpending ? "—" : DayScore switch
    {
        100 => "A+",
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "F"
    };
    public string DayScoreColorHex => DayScore switch
    {
        100 => "#34D399",
        >= 80 => "#6EE7B7",
        >= 60 => "#FBBF24",
        >= 40 => "#F97316",
        _ => "#F87171"
    };
}
