namespace Finora.ViewModels;

public class AffordabilityBillCoverageRow
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Cost per single occurrence of this bill.</summary>
    public decimal WeeklyAmount { get; set; }

    /// <summary>Number of occurrences within the check window.</summary>
    public int OccurrenceCount { get; set; }

    public bool IsCovered { get; set; }

    /// <summary>Worst projected shortfall across all occurrences (positive = money short).</summary>
    public decimal MaxShortfall { get; set; }

    /// <summary>Extra weekly savings needed (on top of budgeted transfer) to cover the worst occurrence.</summary>
    public decimal ExtraWeeklyNeeded { get; set; }

    public string WeeklyAmountDisplay => $"{WeeklyAmount:C}";

    public string OccurrenceDisplay => OccurrenceCount == 1 ? "1 occurrence" : $"{OccurrenceCount} occurrences";

    public string StatusColorHex => IsCovered ? "#6EE7B7" : "#F87171";

    /// <summary>"Covered" or "At risk" — human-friendly, not accounting-speak.</summary>
    public string CoverageDisplay => IsCovered ? "Covered" : "At risk";

    /// <summary>Shortfall amount displayed below the badge when at risk.</summary>
    public string ShortfallDisplay => MaxShortfall <= 0 ? string.Empty : $"{MaxShortfall:C} projected shortfall";

    public bool HasShortfall => !IsCovered && MaxShortfall > 0;

    public string ExtraWeeklyDisplay => ExtraWeeklyNeeded <= 0 ? string.Empty : $"needs +{ExtraWeeklyNeeded:C}/wk";

    public bool HasExtraWeekly => ExtraWeeklyNeeded > 0;
}
