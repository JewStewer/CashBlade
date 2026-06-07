namespace Finora.ViewModels;

public class CashForecastChartRow
{
    public DateTime Date { get; set; }

    public string Label => Date.ToString("dd MMM");

    /// <summary>Short label without zero-padding, e.g. "2 Jun" instead of "02 Jun".</summary>
    public string ShortLabel => Date.ToString("d MMM");

    public decimal ProjectedBalance { get; set; }

    public double Share { get; set; }

    public string ProjectedBalanceDisplay => ProjectedBalance.ToString("C");

    /// <summary>Abbreviated balance, e.g. "$1.8k" or "$799".</summary>
    public string BalanceLabel
    {
        get
        {
            var abs = Math.Abs(ProjectedBalance);
            var prefix = ProjectedBalance < 0 ? "-" : "";
            return abs >= 1000 ? $"{prefix}${abs / 1000:0.#}k" : ProjectedBalance.ToString("C0");
        }
    }

    public string ColorHex => ProjectedBalance < 0 ? "#F87171" : "#6EE7B7";

    /// <summary>Net change on this date vs the previous event date.</summary>
    public decimal NetChange { get; set; }

    public string NetChangeDisplay => NetChange == 0 ? string.Empty
        : NetChange > 0 ? $"+{NetChange:C0}"
        : NetChange.ToString("C0");

    public string NetChangeColorHex => NetChange > 0 ? "#34D399" : "#F87171";
}
