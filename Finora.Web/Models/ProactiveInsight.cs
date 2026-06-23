namespace Finora.Web.Models;

public enum ProactiveInsightSeverity
{
    Info,
    Warning,
    Critical
}

public class ProactiveInsight
{
    public string Key { get; set; } = string.Empty;
    public ProactiveInsightSeverity Severity { get; set; } = ProactiveInsightSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
}
