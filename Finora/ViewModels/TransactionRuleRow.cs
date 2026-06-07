namespace Finora.ViewModels;

public class TransactionRuleRow
{
    public string ContainsText { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
}
