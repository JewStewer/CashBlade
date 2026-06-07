namespace Finora.ViewModels;

public sealed record BudgetSuggestion(
    decimal WeeklyIncome,
    decimal Bills,
    decimal Essentials,
    decimal Savings,
    decimal Unplanned,
    IReadOnlyList<BudgetBreakdownRow> Breakdown);
