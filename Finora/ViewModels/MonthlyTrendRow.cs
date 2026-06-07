namespace Finora.ViewModels;

public class MonthlyTrendRow
{
    public string MonthLabel { get; set; } = string.Empty;

    public decimal Income { get; set; }

    public decimal Spending { get; set; }

    public decimal MaxValue { get; set; }

    public double IncomeBarHeight => MaxValue <= 0 ? 0 : Math.Max((double)(Income / MaxValue) * 88, Income > 0 ? 3 : 0);

    public double SpendingBarHeight => MaxValue <= 0 ? 0 : Math.Max((double)(Spending / MaxValue) * 88, Spending > 0 ? 3 : 0);

    public string IncomeLabelShort => Income <= 0 ? "" : Income >= 1000 ? $"${Income / 1000m:0.0}k" : $"${Income:0}";

    public string SpendingLabelShort => Spending <= 0 ? "" : Spending >= 1000 ? $"${Spending / 1000m:0.0}k" : $"${Spending:0}";

    public bool HasData => Income > 0 || Spending > 0;
}
