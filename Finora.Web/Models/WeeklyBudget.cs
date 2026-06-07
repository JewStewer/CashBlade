namespace Finora.Web.Models;

public class WeeklyBudget
{
    public int Id { get; set; }
    public int IncomeCents { get; set; }
    public decimal IncomeDollars { get => IncomeCents / 100m; set => IncomeCents = (int)Math.Round(value * 100m); }
    public int BillsCents { get; set; }
    public decimal BillsDollars { get => BillsCents / 100m; set => BillsCents = (int)Math.Round(value * 100m); }
    public int EssentialsCents { get; set; }
    public decimal EssentialsDollars { get => EssentialsCents / 100m; set => EssentialsCents = (int)Math.Round(value * 100m); }
    public int SavingsCents { get; set; }
    public decimal SavingsDollars { get => SavingsCents / 100m; set => SavingsCents = (int)Math.Round(value * 100m); }
    public int UnplannedCents { get; set; }
    public decimal UnplannedDollars { get => UnplannedCents / 100m; set => UnplannedCents = (int)Math.Round(value * 100m); }
}
