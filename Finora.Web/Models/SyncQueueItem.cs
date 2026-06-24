namespace Finora.Web.Models;

public class SyncQueueItem
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Kind { get; set; } = string.Empty;
}
