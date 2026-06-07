namespace Finora.ViewModels;

public class AccountProjectionRow
{
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#0F766E";
    public bool IsTotal { get; set; }

    public decimal CurrentBalance { get; set; }
    public decimal BillsBeforePay { get; set; }
    public decimal BillsAfterPay { get; set; }
    public decimal NextIncomeCredit { get; set; }
    public decimal TotalIncomeCredit { get; set; }

    public decimal BeforePayBalance => CurrentBalance - BillsBeforePay;
    public decimal AfterPayBalance => BeforePayBalance + NextIncomeCredit;
    public decimal ForecastEndBalance => CurrentBalance - (BillsBeforePay + BillsAfterPay) + TotalIncomeCredit;

    public string CurrentDisplay => CurrentBalance.ToString("C");
    public string BeforePayDisplay => BeforePayBalance.ToString("C");
    public string AfterPayDisplay => AfterPayBalance.ToString("C");
    public string ForecastEndDisplay => ForecastEndBalance.ToString("C");

    public string CurrentColor => "#E6EDF3";
    public string BeforePayColor => BeforePayBalance < 0 ? "#F87171" : "#E6EDF3";
    public string AfterPayColor => AfterPayBalance < 0 ? "#F87171" : NextIncomeCredit > 0 ? "#34D399" : "#E6EDF3";
    public string ForecastEndColor => ForecastEndBalance < 0 ? "#F87171" : "#34D399";
}
