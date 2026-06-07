namespace Finora.ViewModels;

public class ReportChartRow
{
    public string Label { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public double Share { get; set; }

    public string AmountDisplay => Amount.ToString("C");

    public string ShareDisplay => Share.ToString("P0");

    public string ColorHex { get; set; } = "#0F766E";
}
