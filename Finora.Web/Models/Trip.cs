namespace Finora.Web.Models;

public class Trip
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Destination { get; set; }
    public string? Notes { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? SavingsAccountId { get; set; }
    public int WeeklyContributionCents { get; set; }
    public decimal WeeklyContributionDollars
    {
        get => WeeklyContributionCents / 100m;
        set => WeeklyContributionCents = (int)Math.Round(value * 100m);
    }
    public List<TripItineraryItem> Itinerary { get; set; } = new();
    public List<TripChecklistItem> Checklist { get; set; } = new();
    public List<TripBudgetItem> BudgetItems { get; set; } = new();
}

public class TripItineraryItem
{
    public DateTime Date { get; set; }
    public string? Time { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class TripChecklistItem
{
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
    public DateTime? DueDate { get; set; }
}

public class TripBudgetItem
{
    public string Category { get; set; } = string.Empty;
    public int PlannedCents { get; set; }
    public int ActualCents { get; set; }
    public bool Paid { get; set; }
    public string? Notes { get; set; }
    public decimal PlannedDollars
    {
        get => PlannedCents / 100m;
        set => PlannedCents = (int)Math.Round(value * 100m);
    }
    public decimal ActualDollars
    {
        get => ActualCents / 100m;
        set => ActualCents = (int)Math.Round(value * 100m);
    }
}
