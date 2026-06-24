namespace Finora.Web.Models;

public class BillIntelligenceSuggestion
{
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public RecurringPayment? RecurringPayment { get; set; }
    public int? BillId { get; set; }
}
