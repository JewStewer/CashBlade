namespace Finora.ViewModels;

public class DangerAlertRow
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";

    public string ColorHex => Severity == "Danger"
        ? "#F87171"
        : Severity == "Warning"
            ? "#F59E0B"
            : "#6EE7B7";
}
