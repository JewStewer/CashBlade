using Finora.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Reflection.Emit;

namespace Finora.Data;

public class FinoraDbContext : DbContext
{
    private const string CurrentAppDataFolderName = "Cashglade";
    private const string LegacyAppDataFolderName = "Finora";
    private const string DatabaseFileName = "cashglade.db";
    private const string LegacyDatabaseFileName = "finora.db";

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillOccurrenceStatus> BillOccurrenceStatuses => Set<BillOccurrenceStatus>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<WeeklyBudget> WeeklyBudgets => Set<WeeklyBudget>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public static string DatabasePath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, CurrentAppDataFolderName, DatabaseFileName);
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(localAppData, CurrentAppDataFolderName);

        Directory.CreateDirectory(folder);

        var dbPath = DatabasePath;
        var legacyDbPath = Path.Combine(localAppData, LegacyAppDataFolderName, LegacyDatabaseFileName);
        if (!File.Exists(dbPath) && File.Exists(legacyDbPath))
        {
            File.Copy(legacyDbPath, dbPath);
        }

        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>()
            .HasIndex(a => a.Name)
            .IsUnique();

        modelBuilder.Entity<Account>()
            .HasIndex(a => a.UpAccountId)
            .IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Name)
            .IsUnique();

        modelBuilder.Entity<AppSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        modelBuilder.Entity<Transaction>()
            .Ignore(t => t.AmountDollars);

        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.UpTransactionId)
            .IsUnique();

        modelBuilder.Entity<Account>()
            .Ignore(a => a.TargetDollars);

        modelBuilder.Entity<Account>()
            .Ignore(a => a.TargetStartingBalanceDollars);

        modelBuilder.Entity<Bill>()
            .Ignore(b => b.AmountDollars);

        modelBuilder.Entity<Debt>()
            .Ignore(d => d.BalanceDollars);

        modelBuilder.Entity<Debt>()
            .Ignore(d => d.MinimumPaymentDollars);

        modelBuilder.Entity<Debt>()
            .Ignore(d => d.OriginalBalanceDollars);

        modelBuilder.Entity<DebtPayment>()
            .Ignore(p => p.AmountDollars);

        modelBuilder.Entity<DebtPayment>()
            .HasIndex(p => p.UpTransactionId)
            .IsUnique();

        modelBuilder.Entity<BillOccurrenceStatus>()
            .HasIndex(s => new { s.BillId, s.DueDate })
            .IsUnique();

        modelBuilder.Entity<SavingsGoal>()
            .Ignore(g => g.TargetDollars);

        modelBuilder.Entity<SavingsGoal>()
            .Ignore(g => g.CurrentDollars);

        modelBuilder.Entity<SavingsGoal>()
            .Ignore(g => g.WeeklyContributionDollars);

        modelBuilder.Entity<WeeklyBudget>()
            .Ignore(b => b.IncomeDollars);

        modelBuilder.Entity<WeeklyBudget>()
            .Ignore(b => b.BillsDollars);

        modelBuilder.Entity<WeeklyBudget>()
            .Ignore(b => b.EssentialsDollars);

        modelBuilder.Entity<WeeklyBudget>()
            .Ignore(b => b.SavingsDollars);

        modelBuilder.Entity<WeeklyBudget>()
            .Ignore(b => b.UnplannedDollars);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Bill>()
            .HasOne(b => b.Account)
            .WithMany()
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Bill>()
            .HasOne(b => b.Debt)
            .WithMany()
            .HasForeignKey(b => b.DebtId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<BillOccurrenceStatus>()
            .HasOne(s => s.Bill)
            .WithMany()
            .HasForeignKey(s => s.BillId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DebtPayment>()
            .HasOne(p => p.Debt)
            .WithMany()
            .HasForeignKey(p => p.DebtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
