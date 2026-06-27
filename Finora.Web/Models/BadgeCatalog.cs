namespace Finora.Web.Models;

public record BadgeDefinition(string Id, string Title, string Description);

public static class BadgeCatalog
{
    public static readonly BadgeDefinition[] All =
    {
        new("emergency-1k", "First $1k", "Built a savings account balance of $1,000 or more"),
        new("debt-half", "Halfway There", "Cut a debt balance below half its original amount"),
        new("streak-12", "3 Months Strong", "Reached a 12-week budget streak"),
        new("no-spend-7", "Quiet Week", "Kept No-Spend Mode on for 7 days straight"),
        new("debt-cleared", "Debt Free", "Paid off a debt completely"),
    };
}

public class BadgeState
{
    public List<string> UnlockedBadgeIds { get; set; } = new();
    public Dictionary<string, DateTime> UnlockedDates { get; set; } = new();
}
