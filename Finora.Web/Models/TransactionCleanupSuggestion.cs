namespace Finora.Web.Models;

public class TransactionCleanupSuggestion
{
    public string Merchant { get; set; } = string.Empty;
    public int SuggestedCategoryId { get; set; }
    public string SuggestedCategoryName { get; set; } = string.Empty;
    public int AffectedCount { get; set; }
    public int TotalSeen { get; set; }
    public decimal AffectedAmount { get; set; }
}
