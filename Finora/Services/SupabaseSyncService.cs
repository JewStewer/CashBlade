using Finora.Api;
using Finora.Data;
using Finora.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Finora.Services;

public static class SupabaseSyncService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static event Action? PhoneChangesApplied;

    private static SupabaseConfig? _config;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static volatile bool _pushInProgress;
    private static CancellationTokenSource? _debounceCts;

    public static void Start()
    {
        _config = LoadConfig();
        if (_config is null)
        {
            Log("No supabase.json found — cloud sync disabled.");
            return;
        }

        Log($"Cloud sync enabled. Pushing to {_config.Url} every 5 minutes.");

        FinoraDbContext.Changed += OnLocalChange;

        _ = Task.Run(async () =>
        {
            // Push immediately on startup, then every 5 minutes
            await PushAsync();
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                await PushAsync();
            }
        });
    }

    // Edits anywhere in the app raise FinoraDbContext.Changed. Debounce a few
    // seconds so a burst of saves results in one push, then push so the
    // change reaches iOS without waiting for the 5-minute timer.
    private static void OnLocalChange()
    {
        if (_config is null || _pushInProgress) return;

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await PushAsync();
        });
    }

    public static async Task PushAsync()
    {
        if (_config is null || _pushInProgress) return;
        _pushInProgress = true;
        try
        {
            Log("Cloud sync push started.");
            using var db = new FinoraDbContext();

            // Pull and apply any phone-side changes first so they're included in this push
            var phoneChanges = await ApplyPhonePushAsync(db);
            await db.SaveChangesAsync();
            if (phoneChanges) PhoneChangesApplied?.Invoke();
            // AsNoTracking prevents EF Core from wiring up navigation properties
            // which would cause circular reference errors when serialising to JSON
            var accounts = await db.Accounts.AsNoTracking().ToListAsync();
            var categories = await db.Categories.AsNoTracking().ToListAsync();
            var transactions = await db.Transactions.AsNoTracking().ToListAsync();
            var bills = await db.Bills.AsNoTracking().ToListAsync();
            var billOccurrenceStatuses = await db.BillOccurrenceStatuses.AsNoTracking().ToListAsync();
            var debts = await db.Debts.AsNoTracking().ToListAsync();
            var debtPayments = await db.DebtPayments.AsNoTracking().ToListAsync();
            var savingsGoals = await db.SavingsGoals.AsNoTracking().ToListAsync();
            var weeklyBudgets = await db.WeeklyBudgets.AsNoTracking().ToListAsync();
            var appSettings = await db.AppSettings.AsNoTracking().ToListAsync();
            var trips = await db.Trips.AsNoTracking().ToListAsync();

            if (bills.Count == 0 && debts.Count == 0 && savingsGoals.Count == 0 && weeklyBudgets.Count == 0
                && (accounts.Count > 0 || transactions.Count > 0))
            {
                Log("Cloud sync push skipped: local database has no bills, debts, savings goals, or weekly budgets.");
                return;
            }

            var payload = new
            {
                id = "main",
                syncedAt = DateTime.UtcNow,
                accounts,
                categories,
                transactions,
                bills,
                billOccurrenceStatuses,
                debts,
                debtPayments,
                savingsGoals,
                weeklyBudgets,
                appSettings,
                trips
            };

            var baseUrl = NormaliseUrl(_config.Url);
            var json = JsonSerializer.Serialize(new { id = "main", payload = JsonSerializer.Serialize(payload, _opts), synced_at = DateTime.UtcNow }, _opts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/v1/finance_sync")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddSupabaseHeaders(req, _config.AnonKey);
            req.Headers.Add("Prefer", "resolution=merge-duplicates");

            var resp = await _http.SendAsync(req);
            Log(resp.IsSuccessStatusCode ? "Cloud sync push succeeded." : $"Cloud sync push failed: {resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Log($"Cloud sync push error: {ex.Message}");
        }
        finally
        {
            _pushInProgress = false;
        }
    }

    // ── Read phone-pushed changes from Supabase and apply to local DB ────────────
    private static async Task<bool> ApplyPhonePushAsync(FinoraDbContext db)
    {
        if (_config is null) return false;
        try
        {
            var baseUrl = NormaliseUrl(_config.Url);
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/phone_push?id=eq.main&select=payload");
            AddSupabaseHeaders(req, _config.AnonKey);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<PhonePushRow>>(body, _opts);
            if (rows is null || rows.Count == 0 || string.IsNullOrWhiteSpace(rows[0].Payload)) return false;

            var push = JsonSerializer.Deserialize<PushPayload>(rows[0].Payload, _opts);
            if (push is null) return false;

            // New transactions (phone-created, negative temp IDs)
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

                db.Transactions.Add(new Transaction
                {
                    Date = DateTime.SpecifyKind(t.Date.Date, DateTimeKind.Unspecified),
                    Description = t.Description, AmountCents = t.AmountCents,
                    AccountId = t.AccountId, CategoryId = t.CategoryId,
                    TransferId = t.TransferId, IsUnnecessary = t.IsUnnecessary,
                    UpTransactionId = t.UpTransactionId
                });
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

            // Bill occurrence statuses (paid/unpaid overrides)
            foreach (var s in push.UpdatedBillStatuses)
            {
                var existing = await db.BillOccurrenceStatuses
                    .FirstOrDefaultAsync(x => x.BillId == s.BillId && x.DueDate.Date == s.DueDate.Date);
                if (existing is null)
                    db.BillOccurrenceStatuses.Add(new BillOccurrenceStatus
                    {
                        BillId = s.BillId, DueDate = s.DueDate,
                        IsPaid = s.IsPaid, IsSkipped = s.IsSkipped, PaidOn = s.PaidOn
                    });
                else
                {
                    existing.IsPaid = s.IsPaid;
                    existing.PaidOn = s.PaidOn;
                }

                // Mirror paid status on Bill itself
                var bill = await db.Bills.FindAsync(s.BillId);
                if (bill is not null) bill.IsPaid = s.IsPaid;
            }

            // New debts from phone (negative temp IDs, get real IDs on save)
            var debtIdMap = new Dictionary<int, Debt>();
            foreach (var d in push.NewDebts)
            {
                var entity = new Debt
                {
                    Name = d.Name, BalanceCents = d.BalanceCents,
                    MinimumPaymentCents = d.MinimumPaymentCents, PaymentPeriod = d.PaymentPeriod,
                    InterestRate = d.InterestRate, OriginalBalanceCents = d.OriginalBalanceCents,
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
                    TargetDate = g.TargetDate,
                    GroupName = g.GroupName
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
                existing.GroupName = g.GroupName;
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

            // New bills from phone
            foreach (var b in push.NewBills)
            {
                var entity = new Bill
                {
                    Name = b.Name, AccountId = b.AccountId, AmountCents = b.AmountCents,
                    DueDate = b.DueDate, Frequency = (BillFrequency)(int)b.Frequency, IsAutoPay = b.IsAutoPay
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
                existing.Name = b.Name; existing.AccountId = b.AccountId;
                existing.AmountCents = b.AmountCents; existing.DueDate = b.DueDate;
                existing.Frequency = (BillFrequency)(int)b.Frequency; existing.IsAutoPay = b.IsAutoPay;
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
                    .Where(b => b.AmountCents == deleted.AmountCents && b.Frequency == deleted.Frequency)
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
                    UpTransactionId = p.UpTransactionId, AmountCents = p.AmountCents,
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

            // Updated settings from phone (NextPayDate, CategoryLimit:*, etc.)
            // Mirror of SyncServer.cs so the Supabase path behaves the same as Wi-Fi.
            foreach (var setting in push.UpdatedSettings)
            {
                var existing = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == setting.Key);
                if (existing is null)
                    db.AppSettings.Add(new AppSetting { Key = setting.Key, Value = setting.Value });
                else
                    existing.Value = setting.Value;
            }

            await db.SaveChangesAsync();

            // Delete the phone_push row so it isn't applied again
            var del = new HttpRequestMessage(HttpMethod.Delete,
                $"{baseUrl}/rest/v1/phone_push?id=eq.main");
            AddSupabaseHeaders(del, _config.AnonKey);
            await _http.SendAsync(del);

            Log("Applied phone push from Supabase.");
            return true;
        }
        catch (Exception ex) { Log($"ApplyPhonePush error: {ex.Message}"); return false; }
    }

    private static SupabaseConfig? LoadConfig()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "supabase.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cashglade", "supabase.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<SupabaseConfig>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!string.IsNullOrWhiteSpace(cfg?.Url) && !string.IsNullOrWhiteSpace(cfg?.AnonKey))
                    return cfg;
            }
            catch { }
        }
        return null;
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

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cashglade");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"),
                $"[{DateTimeOffset.Now:O}] SupabaseSync: {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void AddSupabaseHeaders(HttpRequestMessage req, string key)
    {
        req.Headers.Add("apikey", key);
        req.Headers.Add("Authorization", $"Bearer {key}");
    }

    private static string NormaliseUrl(string url)
    {
        url = url.TrimEnd('/');
        if (url.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^8];
        return url;
    }

    private class SupabaseConfig
    {
        public string Url { get; set; } = string.Empty;
        public string AnonKey { get; set; } = string.Empty;
    }

    private class PhonePushRow
    {
        public string Payload { get; set; } = string.Empty;
    }
}
