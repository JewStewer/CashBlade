namespace Finora.ViewModels;

public class BillFundingPlanRow
{
    public string SaverName { get; set; } = string.Empty;

    public decimal WeeklyAmount { get; set; }

    public decimal MonthlyAmount { get; set; }

    public decimal CurrentBalance { get; set; }

    public decimal DueBeforePayday { get; set; }

    public decimal DueOnPayday { get; set; }

    public decimal DueThroughPayday => DueBeforePayday + DueOnPayday;

    public decimal NeededBeforePayday { get; set; }

    public decimal PaydayTopUp { get; set; }

    public decimal PaydayTransfer => Math.Max(WeeklyAmount, PaydayTopUp);

    public int BillCount { get; set; }

    public int DueBeforePaydayCount { get; set; }

    public int DueOnPaydayCount { get; set; }

    public DateTime? NextDueDate { get; set; }

    public string NextDueDisplay => NextDueDate is null ? "No bills scheduled" : NextDueDate.Value.ToString("dd/MM/yyyy");

    public string TransferExplanation => DueThroughPayday > 0
        ? $"{DueThroughPayday:C} due through payday, {CurrentBalance:C} currently in account"
        : $"{WeeklyAmount:C}/week keeps future bills funded";

    public string PaydayStatus => PaydayTopUp > WeeklyAmount
        ? "Top up"
        : "Weekly set-aside";

    public bool HasPrePaydayShortfall => NeededBeforePayday > 0;

    public string PrePaydayDetail => DueBeforePaydayCount == 1
        ? $"{DueBeforePaydayCount} bill totalling {DueBeforePayday:C} is due before payday — account only has {CurrentBalance:C}"
        : $"{DueBeforePaydayCount} bills totalling {DueBeforePayday:C} are due before payday — account only has {CurrentBalance:C}";

    // Before-payday only (used for pre-payday shortfall logic)
    public decimal BalanceBeforePayday => CurrentBalance - DueBeforePayday;

    public string DueBeforePaydayDisplay => DueBeforePayday > 0 ? $"-{DueBeforePayday:C}" : string.Empty;

    public string BalanceBeforePaydayColorHex => BalanceBeforePayday < 0 ? "#F87171" : "#E6EDF3";

    // Through payday (before + on payday) — matches account card "Bills due" display
    public decimal BalanceAfterBills => CurrentBalance - DueThroughPayday;

    public string DueThroughPaydayDisplay => DueThroughPayday > 0 ? $"-{DueThroughPayday:C}" : string.Empty;

    public string BalanceAfterBillsColorHex => BalanceAfterBills < 0 ? "#F87171" : "#E6EDF3";
}
