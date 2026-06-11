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
                        IsUnnecessary = t.IsUnnecessary
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

                // New bills from phone (negative temp IDs)
                foreach (var b in push.NewBills)
                {
                    db.Bills.Add(new Bill
                    {
                        Name = b.Name,
                        AccountId = b.AccountId,
                        AmountCents = b.AmountCents,
                        DueDate = b.DueDate,
                        Frequency = (BillFrequency)(int)b.Frequency,
                        IsAutoPay = b.IsAutoPay
                    });
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
