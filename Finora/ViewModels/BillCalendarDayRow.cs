using System.Collections.ObjectModel;

namespace Finora.ViewModels;

public class BillCalendarDayRow
{
    public DateTime Date { get; set; }

    public int DayNumber => Date.Day;

    public bool IsCurrentMonth { get; set; }

    public bool IsToday => Date.Date == DateTime.Today;

    public bool IsInSelectedPeriod { get; set; }

    public ObservableCollection<BillCalendarBillRow> Bills { get; } = new();

    public decimal Total => Bills.Sum(b => b.Amount);

    public string TotalDisplay => HasBills ? Total.ToString("C") : string.Empty;

    public bool HasBills => Bills.Count > 0;
}
