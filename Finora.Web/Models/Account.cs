namespace Finora.Web.Models;

public enum AccountType { Spending = 0, Savings = 1, Cash = 2, Credit = 3, Bills = 4 }

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UpAccountId { get; set; }
    public AccountType Type { get; set; }
    public string ColorHex { get; set; } = "#0F766E";
    public int? TargetCents { get; set; }
    public decimal? TargetDollars
    {
        get => TargetCents is null ? null : TargetCents / 100m;
        set => TargetCents = value is null ? null : (int)Math.Round(value.Value * 100m);
    }
    public DateTime? TargetDate { get; set; }
    public DateTime? TargetStartDate { get; set; }
    public int? TargetStartingBalanceCents { get; set; }
    public decimal? TargetStartingBalanceDollars
    {
        get => TargetStartingBalanceCents is null ? null : TargetStartingBalanceCents / 100m;
        set => TargetStartingBalanceCents = value is null ? null : (int)Math.Round(value.Value * 100m);
    }
}
