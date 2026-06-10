namespace Finora.ViewModels;

public class BudgetInsightRow
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Nav section to jump to when this tip has an actionable destination, e.g. "Bills".</summary>
    public string? NavTarget { get; set; }

    public bool HasNavTarget => !string.IsNullOrEmpty(NavTarget);

    public string NavLabel => NavTarget switch
    {
        "Planning" => "Go to Insights",
        null or "" => string.Empty,
        _ => $"Go to {NavTarget}"
    };
}
