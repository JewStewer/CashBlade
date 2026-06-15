using Finora.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Finora.Data;

public static class DatabaseInitializer
{
    public static void RepairSchema()
    {
        // Fast path: open a raw SQLite connection (no EF Core model compilation) and check
        // PRAGMA user_version. When the schema is already at the current version we can skip
        // the entire repair — including the expensive first-time EF Core model compilation
        // that would otherwise add ~10-12 seconds to every launch.
        if (IsSchemaVersionCurrent())
            return;

        using var db = new FinoraDbContext();
        SchemaRepair.ApplyStartupCompatibility(db);
    }

    /// <summary>
    /// Checks PRAGMA user_version using a lightweight raw ADO connection so that EF Core is
    /// never instantiated on launches where the schema is already up to date.
    /// </summary>
    private static bool IsSchemaVersionCurrent()
    {
        var dbPath = FinoraDbContext.DatabasePath;
        if (!File.Exists(dbPath))
            return false;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar()) >= SchemaRepair.CurrentSchemaVersion;
        }
        catch
        {
            // If the version check fails for any reason, fall through to the full repair.
            return false;
        }
    }

    public static void Initialize()
    {
        Log("Create DbContext.");
        using var db = new FinoraDbContext();

        try
        {
            Log("EnsureCreated started.");
            db.Database.EnsureCreated();
            Log("EnsureCreated finished.");
        }
        catch
        {
            Log("EnsureCreated failed; continuing with idempotent schema updates.");
            // Older app versions may have partially-created tables. Schema updates below are idempotent.
        }

        Log("EnsureSchemaUpdates started.");
        EnsureSchemaUpdates(db);
        Log("EnsureSchemaUpdates finished.");

        Log("Seed accounts started.");
        AddMissingAccount(db, "Bills", AccountType.Bills, "#2563EB");
        AddMissingAccount(db, "Essentials", AccountType.Spending, "#16A34A");
        AddMissingAccount(db, "Savings", AccountType.Savings, "#7C3AED");
        AddMissingAccount(db, "Emergency", AccountType.Savings, "#DC2626");
        AddMissingAccount(db, "Cash", AccountType.Cash, "#D97706");
        Log("Seed accounts finished.");

        Log("Seed categories started.");
        if (!db.Categories.Any())
        {
            db.Categories.AddRange(
                new Category { Name = "Income", Type = CategoryType.Income },
                new Category { Name = "Groceries", Type = CategoryType.Expense },
                new Category { Name = "Fuel", Type = CategoryType.Expense },
                new Category { Name = "Rent", Type = CategoryType.Expense },
                new Category { Name = "Phone", Type = CategoryType.Expense },
                new Category { Name = "Internet", Type = CategoryType.Expense },
                new Category { Name = "Car Loan", Type = CategoryType.Expense },
                new Category { Name = "Insurance", Type = CategoryType.Expense },
                new Category { Name = "Medical", Type = CategoryType.Expense },
                new Category { Name = "Study", Type = CategoryType.Expense },
                new Category { Name = "Debt", Type = CategoryType.Expense },
                new Category { Name = "Misc", Type = CategoryType.Expense },
                new Category { Name = "Unplanned", Type = CategoryType.Expense },
                new Category { Name = "Transfer", Type = CategoryType.Expense },
                new Category { Name = "Opening Balance", Type = CategoryType.Income },
                new Category { Name = "Balance Adjustment", Type = CategoryType.Income }
            );
        }
        else
        {
            AddMissingCategory(db, "Opening Balance", CategoryType.Income);
            AddMissingCategory(db, "Balance Adjustment", CategoryType.Income);
            AddMissingCategory(db, "Study", CategoryType.Expense);
            AddMissingCategory(db, "Debt", CategoryType.Expense);
            AddMissingCategory(db, "Misc", CategoryType.Expense);
            AddMissingCategory(db, "Transfer", CategoryType.Expense);
        }
        Log("Seed categories finished.");

        Log("Seed settings started.");
        AddMissingSetting(db, "NextPayDate", DateTime.Today.ToString("O"));
        AddMissingSetting(db, "SummaryPeriod", "Monthly");
        Log("Seed settings finished.");

        Log("ConsolidateUpBankAccount started.");
        ConsolidateUpBankAccount(db);
        Log("ConsolidateUpBankAccount finished.");
        Log("CategorizeImportedTransfers started.");
        CategorizeImportedTransfers(db);
        Log("CategorizeImportedTransfers finished.");
        Log("ApplyLearnedTransactionCategories started.");
        ApplyLearnedTransactionCategories(db);
        Log("ApplyLearnedTransactionCategories finished.");
        Log("RemoveLegacyRecurringPaymentBills started.");
        RemoveLegacyRecurringPaymentBills(db);
        Log("RemoveLegacyRecurringPaymentBills finished.");
        Log("InitializeExistingAccountTargets started.");
        InitializeExistingAccountTargets(db);
        Log("InitializeExistingAccountTargets finished.");

        try
        {
            Log("SaveChanges started.");
            db.SaveChanges();
            Log("SaveChanges finished.");
        }
        catch (DbUpdateException)
        {
            Log("SaveChanges failed with DbUpdateException; continuing.");
            // Bad legacy seed state should never stop the app from opening.
        }
    }

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cashglade");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup.log"),
                $"[{DateTimeOffset.Now:O}] DatabaseInitializer: {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void AddMissingAccount(FinoraDbContext db, string name, AccountType type, string colorHex)
    {
        if (!db.Accounts.AsEnumerable().Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            db.Accounts.Add(new Account { Name = name, Type = type, ColorHex = colorHex });
        }
    }

    private static void AddMissingCategory(FinoraDbContext db, string name, CategoryType type)
    {
        if (!db.Categories.Any(c => c.Name == name))
        {
            db.Categories.Add(new Category { Name = name, Type = type });
        }
    }

    private static void AddMissingSetting(FinoraDbContext db, string key, string value)
    {
        if (!db.AppSettings.Any(s => s.Key == key))
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
    }

    private static void ConsolidateUpBankAccount(FinoraDbContext db)
    {
        var upBankAccount = db.Accounts.AsEnumerable().FirstOrDefault(a => string.Equals(a.Name, "Up Bank", StringComparison.OrdinalIgnoreCase));
        if (upBankAccount is null)
        {
            return;
        }

        var spendingAccount = db.Accounts.AsEnumerable().FirstOrDefault(a => string.Equals(a.Name, "Spending", StringComparison.OrdinalIgnoreCase));
        if (spendingAccount is null)
        {
            upBankAccount.Name = "Spending";
            return;
        }

        var upBankTransactions = db.Transactions.Where(t => t.AccountId == upBankAccount.Id).ToList();
        foreach (var transaction in upBankTransactions)
        {
            transaction.AccountId = spendingAccount.Id;
        }

        if (!db.Bills.Any(b => b.AccountId == upBankAccount.Id))
        {
            db.Accounts.Remove(upBankAccount);
        }
    }

    private static void CategorizeImportedTransfers(FinoraDbContext db)
    {
        var transferCategory = db.Categories.FirstOrDefault(c => c.Name == "Transfer");
        if (transferCategory is null)
        {
            return;
        }

        var importedTransfers = db.Transactions
            .Where(t => t.UpTransactionId != null)
            .AsEnumerable()
            .Where(t => TransactionClassification.IsInternalMovementDescription(t.Description))
            .ToList();

        foreach (var transaction in importedTransfers)
        {
            transaction.CategoryId = transferCategory.Id;
        }
    }

    private static void ApplyLearnedTransactionCategories(FinoraDbContext db)
    {
        var transactions = db.Transactions
            .Include(t => t.Category)
            .AsEnumerable()
            .Where(t => !TransactionClassification.IsInternalMovement(t))
            .Where(t => TransactionClassification.GetMerchantKey(t.Description) != string.Empty)
            .GroupBy(t => new
            {
                MerchantKey = TransactionClassification.GetMerchantKey(t.Description),
                IsExpense = t.AmountCents < 0
            })
            .ToList();

        foreach (var group in transactions)
        {
            var learnedCategory = group
                .Where(t => t.Category?.Name is not ("Misc" or "Unplanned"))
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Id)
                .Select(t => t.Category)
                .FirstOrDefault();

            if (learnedCategory is null)
            {
                continue;
            }

            foreach (var transaction in group.Where(t => t.Category?.Name is "Misc" or "Unplanned"))
            {
                transaction.CategoryId = learnedCategory.Id;
            }
        }
    }

    private static void RemoveLegacyRecurringPaymentBills(FinoraDbContext db)
    {
        const string cleanupKey = "RecurringPaymentBillsCleanupDone";
        if (db.AppSettings.Any(s => s.Key == cleanupKey))
        {
            return;
        }

        var recurringCandidates = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .AsEnumerable()
            .Where(IsRecurringCandidateTransaction)
            .GroupBy(t => new
            {
                Name = NormalizeRecurringDescription(t.Description),
                AmountCents = Math.Abs(t.AmountCents),
                AccountId = t.AccountId
            })
            .Where(g => g.Count() >= 2)
            .Select(g => new
            {
                g.Key.Name,
                g.Key.AmountCents,
                g.Key.AccountId
            })
            .ToList();

        if (recurringCandidates.Count > 0)
        {
            var billsToRemove = db.Bills
                .AsEnumerable()
                .Where(b => recurringCandidates.Any(c =>
                    string.Equals(NormalizeRecurringDescription(b.Name), c.Name, StringComparison.OrdinalIgnoreCase) &&
                    b.AmountCents == c.AmountCents &&
                    b.AccountId == c.AccountId))
                .ToList();

            db.Bills.RemoveRange(billsToRemove);
        }

        db.AppSettings.Add(new AppSetting { Key = cleanupKey, Value = DateTime.Today.ToString("O") });
    }

    private static void InitializeExistingAccountTargets(FinoraDbContext db)
    {
        var accounts = db.Accounts
            .Include(a => a.Transactions)
            .Where(a => a.TargetCents != null && a.TargetStartDate == null)
            .ToList();

        foreach (var account in accounts)
        {
            account.TargetStartDate = DateTime.Today;
            account.TargetStartingBalanceDollars = account.Transactions.Sum(t => t.AmountDollars);
        }
    }

    private static bool IsRecurringCandidateTransaction(Transaction transaction)
    {
        return transaction.AmountCents < 0 &&
            !TransactionClassification.IsInternalMovement(transaction) &&
            !string.IsNullOrWhiteSpace(transaction.Description);
    }

    private static string NormalizeRecurringDescription(string description)
    {
        var cleaned = description.Trim();
        var separatorIndex = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            cleaned = cleaned[..separatorIndex];
        }

        return cleaned;
    }

    private static void EnsureSchemaUpdates(FinoraDbContext db)
    {
        SchemaRepair.Apply(db);
        RemoveLegacyTransactionAmountDollarsColumn(db);

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Value TEXT NOT NULL
            );
            """);

        TryExecute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_AppSettings_Key ON AppSettings (Key);");

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS Bills (
                Id INTEGER NOT NULL CONSTRAINT PK_Bills PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                AccountId INTEGER NOT NULL,
                DebtId INTEGER NULL,
                AmountCents INTEGER NOT NULL,
                DueDate TEXT NOT NULL,
                NextPayDate TEXT NOT NULL,
                Frequency INTEGER NOT NULL,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                IsCreatedFromRecurringPayment INTEGER NOT NULL DEFAULT 0,
                PaymentMatchText TEXT NOT NULL DEFAULT '',
                CONSTRAINT FK_Bills_Accounts_AccountId FOREIGN KEY (AccountId) REFERENCES Accounts (Id) ON DELETE CASCADE
            );
            """);

        TryExecute(db, "CREATE INDEX IF NOT EXISTS IX_Bills_AccountId ON Bills (AccountId);");

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS BillOccurrenceStatuses (
                Id INTEGER NOT NULL CONSTRAINT PK_BillOccurrenceStatuses PRIMARY KEY AUTOINCREMENT,
                BillId INTEGER NOT NULL,
                DueDate TEXT NOT NULL,
                IsPaid INTEGER NOT NULL,
                IsSkipped INTEGER NOT NULL DEFAULT 0,
                MatchedTransactionId INTEGER NULL,
                MatchNote TEXT NOT NULL DEFAULT '',
                PaidOn TEXT NULL,
                OriginalTransactionDescription TEXT NULL,
                OriginalTransactionCategoryId INTEGER NULL,
                OriginalTransactionTransferId TEXT NULL,
                CONSTRAINT FK_BillOccurrenceStatuses_Bills_BillId FOREIGN KEY (BillId) REFERENCES Bills (Id) ON DELETE CASCADE
            );
            """);

        TryExecute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_BillOccurrenceStatuses_BillId_DueDate ON BillOccurrenceStatuses (BillId, DueDate);");
        SchemaRepair.Apply(db);

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS Debts (
                Id INTEGER NOT NULL CONSTRAINT PK_Debts PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                BalanceCents INTEGER NOT NULL,
                MinimumPaymentCents INTEGER NOT NULL,
                PaymentPeriod TEXT NOT NULL DEFAULT 'Weekly',
                InterestRate TEXT NULL,
                OriginalBalanceCents INTEGER NOT NULL,
                UpPaymentMatchText TEXT NULL
            );
            """);

        SchemaRepair.Apply(db);

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS DebtPayments (
                Id INTEGER NOT NULL CONSTRAINT PK_DebtPayments PRIMARY KEY AUTOINCREMENT,
                DebtId INTEGER NOT NULL,
                UpTransactionId TEXT NOT NULL,
                AmountCents INTEGER NOT NULL,
                PaidOn TEXT NOT NULL,
                Description TEXT NOT NULL,
                CONSTRAINT FK_DebtPayments_Debts_DebtId FOREIGN KEY (DebtId) REFERENCES Debts (Id) ON DELETE CASCADE
            );
            """);

        TryExecute(db, "CREATE INDEX IF NOT EXISTS IX_DebtPayments_DebtId ON DebtPayments (DebtId);");
        TryExecute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DebtPayments_UpTransactionId ON DebtPayments (UpTransactionId);");

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS SavingsGoals (
                Id INTEGER NOT NULL CONSTRAINT PK_SavingsGoals PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                TargetCents INTEGER NOT NULL,
                CurrentCents INTEGER NOT NULL,
                WeeklyContributionCents INTEGER NOT NULL,
                TargetDate TEXT NULL
            );
            """);
        SchemaRepair.Apply(db);

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS Trips (
                Id INTEGER NOT NULL CONSTRAINT PK_Trips PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Destination TEXT NULL,
                Notes TEXT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                SavingsAccountId INTEGER NULL,
                WeeklyContributionCents INTEGER NOT NULL DEFAULT 0,
                Itinerary TEXT NOT NULL DEFAULT '[]',
                Checklist TEXT NOT NULL DEFAULT '[]',
                BudgetItems TEXT NOT NULL DEFAULT '[]'
            );
            """);
        SchemaRepair.Apply(db);

        TryExecute(db, """
            CREATE TABLE IF NOT EXISTS WeeklyBudgets (
                Id INTEGER NOT NULL CONSTRAINT PK_WeeklyBudgets PRIMARY KEY AUTOINCREMENT,
                IncomeCents INTEGER NOT NULL,
                BillsCents INTEGER NOT NULL,
                EssentialsCents INTEGER NOT NULL,
                SavingsCents INTEGER NOT NULL,
                UnplannedCents INTEGER NOT NULL
            );
            """);
    }

    private static void RemoveLegacyTransactionAmountDollarsColumn(FinoraDbContext db)
    {
        if (!ColumnExists(db, "Transactions", "AmountDollars"))
        {
            return;
        }

        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF;");

        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS Transactions_New;");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE Transactions_New (
                Id INTEGER NOT NULL CONSTRAINT PK_Transactions PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Description TEXT NOT NULL,
                AmountCents INTEGER NOT NULL,
                AccountId INTEGER NOT NULL,
                CategoryId INTEGER NOT NULL,
                TransferId TEXT NULL,
                UpTransactionId TEXT NULL,
                CONSTRAINT FK_Transactions_Accounts_AccountId FOREIGN KEY (AccountId) REFERENCES Accounts (Id) ON DELETE RESTRICT,
                CONSTRAINT FK_Transactions_Categories_CategoryId FOREIGN KEY (CategoryId) REFERENCES Categories (Id) ON DELETE RESTRICT
            );
            """);
        db.Database.ExecuteSqlRaw("""
            INSERT INTO Transactions_New (Id, Date, Description, AmountCents, AccountId, CategoryId, TransferId, UpTransactionId)
            SELECT Id, Date, Description, AmountCents, AccountId, CategoryId, TransferId, NULL
            FROM Transactions;
            """);
        db.Database.ExecuteSqlRaw("DROP TABLE Transactions;");
        db.Database.ExecuteSqlRaw("ALTER TABLE Transactions_New RENAME TO Transactions;");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Transactions_AccountId ON Transactions (AccountId);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Transactions_CategoryId ON Transactions (CategoryId);");

        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
    }

    private static bool ColumnExists(FinoraDbContext db, string tableName, string columnName)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        db.Database.OpenConnection();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void TryExecute(FinoraDbContext db, string sql)
    {
        try
        {
            db.Database.ExecuteSqlRaw(sql);
        }
        catch
        {
            // SQLite has no portable IF NOT EXISTS for ADD COLUMN.
        }
    }
}
