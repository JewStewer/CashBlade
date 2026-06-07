namespace Finora.ViewModels;

public class CashForecastRow
{
    public DateTime Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Change { get; set; }

    public decimal ProjectedBalance { get; set; }

    public string ChangeDisplay => Change.ToString("C");

    public string ProjectedBalanceDisplay => ProjectedBalance.ToString("C");

    public string ColorHex => ProjectedBalance < 0 ? "#F87171" : "#CBD5E1";
}
