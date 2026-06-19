using Finora.Data;
using Finora.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Finora.Api;

public static class SyncServer
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Start(string? webAppRoot = null)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls("http://0.0.0.0:5050");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseCors();

        // ── Serve published Blazor WASM static files ─────────────────────────
        if (!string.IsNullOrEmpty(webAppRoot) && Directory.Exists(webAppRoot))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webAppRoot),
                RequestPath = string.Empty
            });
        }

        // ── Health check ─────────────────────────────────────────────────────
        app.MapGet("/api/ping", () => Results.Ok(new { ok = true, time = DateTime.UtcNow }));

        // ── Full sync export ──────────────────────────────────────────────────
        app.MapGet("/api/sync", async (HttpContext ctx) =>
        {
            try
            {
                using var db = new FinoraDbContext();
                var payload = new SyncPayload
                {
                    Accounts = await db.Accounts.ToListAsync(),
                    Categories = await db.Categories.ToListAsync(),
                    Transactions = await db.Transactions.ToListAsync(),
                    Bills = await db.Bills.ToListAsync(),
                    BillOccurrenceStatuses = await db.BillOccurrenceStatuses.ToListAsync(),
                    Debts = await db.Debts.ToListAsync(),
                    DebtPayments = await db.DebtPayments.ToListAsync(),
                    SavingsGoals = await db.SavingsGoals.ToListAsync(),
                    WeeklyBudgets = await db.WeeklyBudgets.ToListAsync(),
                    AppSettings = await db.AppSettings.ToListAsync(),
                    Trips = await db.Trips.ToListAsync(),
                    SyncedAt = DateTime.UtcNow
                };
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload, _opts));
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
            }
        });

        // ── Receive phone changes ─────────────────────────────────────────────
        app.MapPost("/api/sync/push", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var push = JsonSerializer.Deserialize<PushPayload>(body, _opts);
                if (push is null) { ctx.Response.StatusCode = 400; return; }

                using var db = new FinoraDbContext();

                // New transactions (negative IDs from phone, get real IDs)
                foreach (var t in push.NewTransactions)
                {
                    // Up Bank transactions carry a stable UpTransactionId — skip if this
                    // exact transaction already exists (e.g. WPF's own Up Bank sync beat
                    // the phone to it, or this push is being reprocessed after a retry).
                    if (!string.IsNullOrWhiteSpace(t.UpTransactionId) &&
                        await db.Transactions.AnyAsync(x => x.UpTransactionId == t.UpTransactionId))
                    {
                        continue;
                    }

                    var entity = new Transaction
                    {
                        // Normalise date: phone serialises DateTime.Today with a local-timezone
                        // offset (e.g. +10:00). System.Text.Json converts that to UTC, which can
                        // shift the calendar date. Taking .Date then re-specifying Unspecified
                        // discards any time/offset so EF Core stores the correct local date.
                        Date = DateTime.SpecifyKind(t.Date.Date, DateTimeKind.Unspecified),
                        Description = t.Description,
                        AmountCents = t.AmountCents,
                        AccountId = t.AccountId,
                        CategoryId = t.CategoryId,
                        TransferId = t.TransferId,
                        IsUnnecessary = t.IsUnnecessary,
                        UpTransactionId = t.UpTransactionId
                    };
                    db.Transactions.Add(entity);
                }

                // Updated transactions
                foreach (var t in push.UpdatedTransactions.Where(x => x.Id > 0))
                {
                    var existing = await db.Transactions.FindAsync(t.Id);
                    if (existing is null) continue;
                    existing.Description = t.Description;
                    existing.AmountCents = t.AmountCents;
                    existing.Date = DateTime.SpecifyKind(t.Date.Date, DateTimeKind.Unspecified);
                    existing.AccountId = t.AccountId;
                    existing.CategoryId = t.CategoryId;
                    existing.IsUnnecessary = t.IsUnnecessary;
                }
                foreach (var edit in push.TransactionEdits)
                {
                    var existing = await FindTransactionAsync(db, edit);
                    if (existing is null) continue;
                    existing.Description = edit.Description;
                    existing.AmountCents = edit.AmountCents;
                    existing.Date = DateTime.SpecifyKind(edit.Date.Date, DateTimeKind.Unspecified);
                    existing.AccountId = edit.AccountId;
                    existing.CategoryId = await ResolveCategoryIdAsync(db, edit.CategoryName, edit.CategoryId, edit.AmountCents);
                    existing.TransferId = edit.TransferId;
                    existing.UpTransactionId = edit.UpTransactionId;
                    existing.IsUnnecessary = edit.IsUnnecessary;
                }

                // Deleted transactions
                foreach (var id in push.DeletedTransactionIds)
                {
                    var existing = await db.Transactions.FindAsync(id);
                    if (existing is not null) db.Transactions.Remove(existing);
                }
                foreach (var deleted in push.DeletedTransactions)
                {
                    var existing = await FindTransactionAsync(db, deleted);
                    if (existing is not null) db.Transactions.Remove(existing);
                }

                // Bill occurrence statuses
                foreach (var s in push.UpdatedBillStatuses)
                {
                    var existing = await db.BillOccurrenceStatuses
                        .FirstOrDefaultAsync(x => x.BillId == s.BillId && x.DueDate.Date == s.DueDate.Date);
                    if (existing is null)
                    {
                        db.BillOccurrenceStatuses.Add(new BillOccurrenceStatus
                        {
                            BillId = s.BillId,
                            DueDate = s.DueDate,
                            IsPaid = s.IsPaid,
                            IsSkipped = s.IsSkipped,
                            PaidOn = s.PaidOn,
                            MatchNote = s.MatchNote
                        });
                    }
                    else
                    {
                        existing.IsPaid = s.IsPaid;
                        existing.IsSkipped = s.IsSkipped;
                        existing.PaidOn = s.PaidOn;
                    }
                    // Mirror paid status on the Bill entity itself (consistent with Supabase path)
                    var bill = await db.Bills.FindAsync(s.BillId);
                    if (bill is not null) bill.IsPaid = s.IsPaid;
                }

                // New debts from phone (negative temp IDs, get real IDs on save)
                var debtIdMap = new Dictionary<int, Debt>();
                foreach (var d in push.NewDebts)
                {
                    var entity = new Debt
                    {
                        Name = d.Name,
                        BalanceCents = d.BalanceCents,
                        MinimumPaymentCents = d.MinimumPaymentCents,
                        PaymentPeriod = d.PaymentPeriod,
                        InterestRate = d.InterestRate,
                        OriginalBalanceCents = d.OriginalBalanceCents,
                        UpPaymentMatchText = d.UpPaymentMatchText
                    };
                    db.Debts.Add(entity);
                    debtIdMap[d.Id] = entity;
                }

                // Updated debts from phone
                foreach (var d in push.UpdatedDebts.Where(x => x.Id > 0))
                {
                    var existing = await db.Debts.FindAsync(d.Id);
                    if (existing is null) continue;
                    existing.Name = d.Name;
                    existing.BalanceCents = d.BalanceCents;
                    existing.MinimumPaymentCents = d.MinimumPaymentCents;
                    existing.PaymentPeriod = d.PaymentPeriod;
                    existing.InterestRate = d.InterestRate;
                    existing.OriginalBalanceCents = d.OriginalBalanceCents;
                }

                // Deleted debts from phone (cascade payments, unlink bills)
                foreach (var id in push.DeletedDebtIds.Where(id => id > 0))
                {
                    var existing = await db.Debts.FindAsync(id);
                    if (existing is null) continue;
                    var payments = await db.DebtPayments.Where(p => p.DebtId == id).ToListAsync();
                    db.DebtPayments.RemoveRange(payments);
                    var linkedBills = await db.Bills.Where(b => b.DebtId == id).ToListAsync();
                    foreach (var linkedBill in linkedBills) linkedBill.DebtId = null;
                    db.Debts.Remove(existing);
                }

                // New savings goals from phone (negative temp IDs, get real IDs on save)
                foreach (var g in push.NewSavingsGoals)
                {
                    db.SavingsGoals.Add(new SavingsGoal
                    {
                        Name = g.Name,
                        TargetCents = g.TargetCents,
                        CurrentCents = g.CurrentCents,
                        WeeklyContributionCents = g.WeeklyContributionCents,
                        TargetDate = g.TargetDate
                    });
                }

                // Updated savings goals from phone
                foreach (var g in push.UpdatedSavingsGoals.Where(x => x.Id > 0))
                {
                    var existing = await db.SavingsGoals.FindAsync(g.Id);
                    if (existing is null) continue;
                    existing.Name = g.Name;
                    existing.TargetCents = g.TargetCents;
                    existing.CurrentCents = g.CurrentCents;
                    existing.WeeklyContributionCents = g.WeeklyContributionCents;
                    existing.TargetDate = g.TargetDate;
                }

                // Deleted savings goals from phone
                foreach (var id in push.DeletedSavingsGoalIds.Where(id => id > 0))
                {
                    var existing = await db.SavingsGoals.FindAsync(id);
                    if (existing is not null) db.SavingsGoals.Remove(existing);
                }

                // New trips from phone (negative temp IDs, get real IDs on save)
                foreach (var t in push.NewTrips)
                {
                    db.Trips.Add(new Trip
                    {
                        Name = t.Name,
                        Destination = t.Destination,
                        Notes = t.Notes,
                        StartDate = t.StartDate,
                        EndDate = t.EndDate,
                        SavingsAccountId = t.SavingsAccountId,
                        WeeklyContributionCents = t.WeeklyContributionCents,
                        Itinerary = t.Itinerary,
                        Checklist = t.Checklist,
                        BudgetItems = t.BudgetItems
                    });
                }

                // Updated trips from phone
                foreach (var t in push.UpdatedTrips.Where(x => x.Id > 0))
                {
                    var existing = await db.Trips.FindAsync(t.Id);
                    if (existing is null) continue;
                    existing.Name = t.Name;
                    existing.Destination = t.Destination;
                    existing.Notes = t.Notes;
                    existing.StartDate = t.StartDate;
                    existing.EndDate = t.EndDate;
                    existing.SavingsAccountId = t.SavingsAccountId;
                    existing.WeeklyContributionCents = t.WeeklyContributionCents;
                    existing.Itinerary = t.Itinerary;
                    existing.Checklist = t.Checklist;
                    existing.BudgetItems = t.BudgetItems;
                }

                // Deleted trips from phone
                foreach (var id in push.DeletedTripIds.Where(id => id > 0))
                {
                    var existing = await db.Trips.FindAsync(id);
                    if (existing is not null) db.Trips.Remove(existing);
                }

                // New bills from phone (negative temp IDs)
                foreach (var b in push.NewBills)
                {
                    var entity = new Bill
                    {
                        Name = b.Name,
                        AccountId = b.AccountId,
                        AmountCents = b.AmountCents,
                        DueDate = b.DueDate,
                        Frequency = (BillFrequency)(int)b.Frequency,
                        IsAutoPay = b.IsAutoPay
                    };
                    if (b.DebtId is { } newBillDebtId)
                    {
                        if (debtIdMap.TryGetValue(newBillDebtId, out var newDebtForBill))
                            entity.Debt = newDebtForBill;
                        else if (newBillDebtId > 0)
                            entity.DebtId = newBillDebtId;
                    }
                    db.Bills.Add(entity);
                }

                // Updated bills from phone
                foreach (var b in push.UpdatedBills.Where(x => x.Id > 0))
                {
                    var existing = await db.Bills.FindAsync(b.Id);
                    if (existing is null) continue;
                    existing.Name = b.Name;
                    existing.AccountId = b.AccountId;
                    existing.AmountCents = b.AmountCents;
                    existing.DueDate = b.DueDate;
                    existing.Frequency = (BillFrequency)(int)b.Frequency;
                    existing.IsAutoPay = b.IsAutoPay;
                }

                // Deleted bills from phone
                foreach (var id in push.DeletedBillIds.Where(id => id > 0))
                {
                    var existing = await db.Bills.FindAsync(id);
                    if (existing is null) continue;
                    var statuses = await db.BillOccurrenceStatuses.Where(s => s.BillId == id).ToListAsync();
                    db.BillOccurrenceStatuses.RemoveRange(statuses);
                    db.Bills.Remove(existing);
                }
                foreach (var deleted in push.DeletedBills)
                {
                    var matches = await db.Bills
                        .AsNoTracking()
                        .Where(b => b.AmountCents == deleted.AmountCents && b.Frequency == (BillFrequency)(int)deleted.Frequency)
                        .ToListAsync();
                    foreach (var match in matches.Where(b => SameBillDelete(b, deleted)))
                    {
                        var existing = await db.Bills.FindAsync(match.Id);
                        if (existing is null) continue;
                        var statuses = await db.BillOccurrenceStatuses.Where(s => s.BillId == existing.Id).ToListAsync();
                        db.BillOccurrenceStatuses.RemoveRange(statuses);
                        db.Bills.Remove(existing);
                    }
                }

                // New debt payments from phone (negative temp IDs; DebtId may
                // reference a debt created earlier in this same push)
                foreach (var p in push.NewDebtPayments)
                {
                    if (!string.IsNullOrWhiteSpace(p.UpTransactionId) &&
                        await db.DebtPayments.AnyAsync(x => x.UpTransactionId == p.UpTransactionId))
                    {
                        continue;
                    }

                    if (p.Id > 0 && await db.DebtPayments.AnyAsync(x => x.Id == p.Id))
                    {
                        continue;
                    }

                    var entity = new DebtPayment
                    {
                        UpTransactionId = p.UpTransactionId,
                        AmountCents = p.AmountCents,
                        PaidOn = DateTime.SpecifyKind(p.PaidOn.Date, DateTimeKind.Unspecified),
                        Description = p.Description
                    };
                    if (debtIdMap.TryGetValue(p.DebtId, out var debtForPayment))
                        entity.Debt = debtForPayment;
                    else
                        entity.DebtId = p.DebtId;
                    db.DebtPayments.Add(entity);
                }

                // Deleted debt payments from phone
                foreach (var id in push.DeletedDebtPaymentIds.Where(id => id > 0))
                {
                    var existing = await db.DebtPayments.FindAsync(id);
                    if (existing is not null) db.DebtPayments.Remove(existing);
                }

                // Updated accounts from phone (savings-goal targets)
                foreach (var a in push.UpdatedAccounts.Where(x => x.Id > 0))
                {
                    var existing = await db.Accounts.FindAsync(a.Id);
                    if (existing is null) continue;
                    existing.TargetCents = a.TargetCents;
                    existing.TargetDate = a.TargetDate;
                    existing.TargetStartDate = a.TargetStartDate;
                    existing.TargetStartingBalanceCents = a.TargetStartingBalanceCents;
                }

                // Updated settings
                foreach (var setting in push.UpdatedSettings)
                {
                    var existing = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == setting.Key);
                    if (existing is null)
                        db.AppSettings.Add(new AppSetting { Key = setting.Key, Value = setting.Value });
                    else
                        existing.Value = setting.Value;
                }

                await db.SaveChangesAsync();
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
            }
        });

        // Fallback to index.html for SPA routing
        if (!string.IsNullOrEmpty(webAppRoot) && Directory.Exists(webAppRoot))
        {
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webAppRoot)
            });
        }

        _ = app.RunAsync();
    }

    private static async Task<Transaction?> FindTransactionAsync(FinoraDbContext db, TransactionEdit edit)
    {
        if (!string.IsNullOrWhiteSpace(edit.UpTransactionId))
        {
            var byUpId = await db.Transactions.FirstOrDefaultAsync(t => t.UpTransactionId == edit.UpTransactionId);
            if (byUpId is not null) return byUpId;
        }

        return await db.Transactions.FindAsync(edit.Id) ??
            await db.Transactions.FirstOrDefaultAsync(t =>
                t.Date.Date == edit.Date.Date &&
                t.AmountCents == edit.AmountCents &&
                t.Description == edit.Description);
    }

    private static async Task<Transaction?> FindTransactionAsync(FinoraDbContext db, TransactionDelete deleted)
    {
        if (!string.IsNullOrWhiteSpace(deleted.UpTransactionId))
        {
            var byUpId = await db.Transactions.FirstOrDefaultAsync(t => t.UpTransactionId == deleted.UpTransactionId);
            if (byUpId is not null) return byUpId;
        }

        return await db.Transactions.FindAsync(deleted.Id) ??
            await db.Transactions.FirstOrDefaultAsync(t =>
                t.Date.Date == deleted.Date.Date &&
                t.AmountCents == deleted.AmountCents &&
                t.Description == deleted.Description);
    }

    private static async Task<int> ResolveCategoryIdAsync(FinoraDbContext db, string categoryName, int categoryId, int amountCents)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var byName = await db.Categories.FirstOrDefaultAsync(c => c.Name == categoryName.Trim());
            if (byName is not null) return byName.Id;
        }

        if (await db.Categories.AnyAsync(c => c.Id == categoryId)) return categoryId;
        var fallbackName = amountCents > 0 ? "Income" : "Misc";
        var fallback = await db.Categories.FirstOrDefaultAsync(c => c.Name == fallbackName);
        if (fallback is not null) return fallback.Id;

        var created = new Category { Name = fallbackName, Type = amountCents > 0 ? CategoryType.Income : CategoryType.Expense };
        db.Categories.Add(created);
        await db.SaveChangesAsync();
        return created.Id;
    }

    private static bool SameBillDelete(Bill bill, BillDelete deleted)
    {
        if (bill.Id > 0 && deleted.Id > 0 && bill.Id == deleted.Id) return true;
        if (bill.AmountCents != deleted.AmountCents) return false;
        if (bill.Frequency != deleted.Frequency) return false;
        if (!string.Equals(bill.Name.Trim(), deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return Math.Abs((bill.DueDate.Date - deleted.DueDate.Date).TotalDays) <= 7;
    }
}

// ── DTOs shared with Blazor (matching JSON shape) ────────────────────────────
public class SyncPayload
{
    public List<Account> Accounts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<Bill> Bills { get; set; } = new();
    public List<BillOccurrenceStatus> BillOccurrenceStatuses { get; set; } = new();
    public List<Debt> Debts { get; set; } = new();
    public List<DebtPayment> DebtPayments { get; set; } = new();
    public List<SavingsGoal> SavingsGoals { get; set; } = new();
    public List<WeeklyBudget> WeeklyBudgets { get; set; } = new();
    public List<AppSetting> AppSettings { get; set; } = new();
    public List<Trip> Trips { get; set; } = new();
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public class PushPayload
{
    public List<Transaction> NewTransactions { get; set; } = new();
    public List<Transaction> UpdatedTransactions { get; set; } = new();
    public List<int> DeletedTransactionIds { get; set; } = new();
    public List<TransactionEdit> TransactionEdits { get; set; } = new();
    public List<TransactionDelete> DeletedTransactions { get; set; } = new();
    public List<BillOccurrenceStatus> UpdatedBillStatuses { get; set; } = new();
    public List<AppSetting> UpdatedSettings { get; set; } = new();
    public List<Bill> NewBills { get; set; } = new();
    public List<Bill> UpdatedBills { get; set; } = new();
    public List<int> DeletedBillIds { get; set; } = new();
    public List<BillDelete> DeletedBills { get; set; } = new();
    public List<Debt> NewDebts { get; set; } = new();
    public List<Debt> UpdatedDebts { get; set; } = new();
    public List<int> DeletedDebtIds { get; set; } = new();
    public List<DebtPayment> NewDebtPayments { get; set; } = new();
    public List<int> DeletedDebtPaymentIds { get; set; } = new();
    public List<Account> UpdatedAccounts { get; set; } = new();
    public List<SavingsGoal> NewSavingsGoals { get; set; } = new();
    public List<SavingsGoal> UpdatedSavingsGoals { get; set; } = new();
    public List<int> DeletedSavingsGoalIds { get; set; } = new();
    public List<Trip> NewTrips { get; set; } = new();
    public List<Trip> UpdatedTrips { get; set; } = new();
    public List<int> DeletedTripIds { get; set; } = new();
}

public class TransactionEdit
{
    public int Id { get; set; }
    public string? UpTransactionId { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public Guid? TransferId { get; set; }
    public bool IsUnnecessary { get; set; }
}

public class TransactionDelete
{
    public int Id { get; set; }
    public string? UpTransactionId { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
}

public class BillDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public DateTime DueDate { get; set; }
    public BillFrequency Frequency { get; set; }
}
