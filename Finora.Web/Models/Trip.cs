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
    // Stable identity so edits target the right item even if the list is
    // replaced or reordered by a background sync while the user has it open.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }
    public string? Time { get; set; }
    public string? EndTime { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int AmountCents { get; set; }
    public decimal AmountDollars
    {
        get => AmountCents / 100m;
        set => AmountCents = (int)Math.Round(value * 100m);
    }
    // When set, AmountCents is an allocation drawn from this TripBudgetItem
    // rather than an independently-typed figure, so schedule and budget totals
    // can't drift apart. Null means the amount was typed directly (legacy/manual).
    public string? BudgetItemId { get; set; }
}

public class TripChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
    public DateTime? DueDate { get; set; }
}

public class TripBudgetItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
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
