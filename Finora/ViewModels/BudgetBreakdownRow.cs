namespace Finora.ViewModels;

public sealed class BudgetBreakdownRow : ViewModelBase
{
    private bool _isIncluded = true;
    private string _transferTo = string.Empty;

    public string Bucket { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Detail { get; init; } = string.Empty;

    public string TransferTo
    {
        get => _transferTo;
        set => SetProperty(ref _transferTo, value);
    }

    public string ExclusionKey { get; init; } = string.Empty;

    public int? AccountId { get; init; }

    public int? BillId { get; init; }

    public int? SavingsGoalId { get; init; }

    public bool IsAccountTarget => AccountId.HasValue;

    public bool IsDefaultIncluded { get; init; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }
}
