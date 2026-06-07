using System.Collections.ObjectModel;

namespace Finora.ViewModels;

public sealed class BudgetBreakdownGroup
{
    public string Bucket { get; init; } = string.Empty;

    public decimal Total { get; init; }

    public decimal ExcludedTotal { get; init; }

    public ObservableCollection<BudgetBreakdownRow> Rows { get; init; } = new();
}
