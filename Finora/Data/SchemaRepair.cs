using Microsoft.EntityFrameworkCore;

namespace Finora.Data;

public static class SchemaRepair
{
    /// <summary>
    /// Increment this whenever new columns/tables are added to ApplyStartupCompatibility.
    /// DatabaseInitializer.RepairSchema() checks PRAGMA user_version against this value and
    /// skips the entire repair (including EF Core model compilation) when already current.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public static void ApplyStartupCompatibility(FinoraDbContext db)
    {
        Execute(db, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Value TEXT NOT NULL
            );
            """);

        Execute(db, """
            CREATE TABLE IF NOT EXISTS WeeklyBudgets (
                Id INTEGER NOT NULL CONSTRAINT PK_WeeklyBudgets PRIMARY KEY AUTOINCREMENT,
                IncomeCents INTEGER NOT NULL DEFAULT 0,
                BillsCents INTEGER NOT NULL DEFAULT 0,
                EssentialsCents INTEGER NOT NULL DEFAULT 0,
                SavingsCents INTEGER NOT NULL DEFAULT 0,
                UnplannedCents INTEGER NOT NULL DEFAULT 0
            );
            """);

        Execute(db, """
            CREATE TABLE IF NOT EXISTS Trips (
                Id INTEGER NOT NULL CONSTRAINT PK_Trips PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Destination TEXT NULL,
                Notes TEXT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                Itinerary TEXT NOT NULL DEFAULT '[]',
                Checklist TEXT NOT NULL DEFAULT '[]',
                BudgetItems TEXT NOT NULL DEFAULT '[]'
            );
            """);

        // Batch PRAGMA table_info queries — one per table instead of one per column.
        // This reduces 22 round-trips to 7 and avoids repeated connection-open overhead.
        var accountCols = GetColumns(db, "Accounts");
        var billCols    = GetColumns(db, "Bills");
        var bosCols     = GetColumns(db, "BillOccurrenceStatuses");
        var txCols      = GetColumns(db, "Transactions");
        var debtCols    = GetColumns(db, "Debts");
        var goalCols    = GetColumns(db, "SavingsGoals");
        var tripCols    = GetColumns(db, "Trips");
        var budgetCols  = GetColumns(db, "WeeklyBudgets");

        AddColumn(db, "Accounts", "ColorHex",                    "TEXT NOT NULL DEFAULT '#0F766E'", accountCols);
        AddColumn(db, "Accounts", "TargetCents",                  "INTEGER NULL",                   accountCols);
        AddColumn(db, "Accounts", "TargetDate",                   "TEXT NULL",                      accountCols);
        AddColumn(db, "Accounts", "TargetStartDate",              "TEXT NULL",                      accountCols);
        AddColumn(db, "Accounts", "TargetStartingBalanceCents",   "INTEGER NULL",                   accountCols);
        AddColumn(db, "Accounts", "UpAccountId",                  "TEXT NULL",                      accountCols);

        AddColumn(db, "Bills", "IsPaid",                          "INTEGER NOT NULL DEFAULT 0",     billCols);
        AddColumn(db, "Bills", "IsCreatedFromRecurringPayment",   "INTEGER NOT NULL DEFAULT 0",     billCols);
        AddColumn(db, "Bills", "DebtId",                          "INTEGER NULL",                   billCols);
        AddColumn(db, "Bills", "IsAutoPay",                       "INTEGER NOT NULL DEFAULT 0",     billCols);
        AddColumn(db, "Bills", "PaymentMatchText",                "TEXT NOT NULL DEFAULT ''",       billCols);

        AddColumn(db, "BillOccurrenceStatuses", "IsSkipped",                          "INTEGER NOT NULL DEFAULT 0", bosCols);
        AddColumn(db, "BillOccurrenceStatuses", "MatchedTransactionId",               "INTEGER NULL",               bosCols);
        AddColumn(db, "BillOccurrenceStatuses", "MatchNote",                          "TEXT NOT NULL DEFAULT ''",   bosCols);
        AddColumn(db, "BillOccurrenceStatuses", "PaidOn",                             "TEXT NULL",                  bosCols);
        AddColumn(db, "BillOccurrenceStatuses", "OriginalTransactionDescription",     "TEXT NULL",                  bosCols);
        AddColumn(db, "BillOccurrenceStatuses", "OriginalTransactionCategoryId",      "INTEGER NULL",               bosCols);
        AddColumn(db, "BillOccurrenceStatuses", "OriginalTransactionTransferId",      "TEXT NULL",                  bosCols);

        AddColumn(db, "Transactions", "UpTransactionId",   "TEXT NULL",                    txCols);
        AddColumn(db, "Transactions", "IsUnnecessary",     "INTEGER NOT NULL DEFAULT 0",   txCols);

        AddColumn(db, "Debts", "UpPaymentMatchText", "TEXT NULL",                      debtCols);
        AddColumn(db, "Debts", "PaymentPeriod",      "TEXT NOT NULL DEFAULT 'Weekly'", debtCols);

        AddColumn(db, "SavingsGoals", "TargetDate", "TEXT NULL", goalCols);

        AddColumn(db, "Trips", "Notes", "TEXT NULL", tripCols);
        AddColumn(db, "Trips", "SavingsAccountId", "INTEGER NULL", tripCols);
        AddColumn(db, "Trips", "WeeklyContributionCents", "INTEGER NOT NULL DEFAULT 0", tripCols);

        AddColumn(db, "WeeklyBudgets", "IncomeCents",      "INTEGER NOT NULL DEFAULT 0", budgetCols);
        AddColumn(db, "WeeklyBudgets", "BillsCents",       "INTEGER NOT NULL DEFAULT 0", budgetCols);
        AddColumn(db, "WeeklyBudgets", "EssentialsCents",  "INTEGER NOT NULL DEFAULT 0", budgetCols);
        AddColumn(db, "WeeklyBudgets", "SavingsCents",     "INTEGER NOT NULL DEFAULT 0", budgetCols);
        AddColumn(db, "WeeklyBudgets", "UnplannedCents",   "INTEGER NOT NULL DEFAULT 0", budgetCols);

        // Stamp the schema version so DatabaseInitializer.RepairSchema() can skip all of
        // the above (including EF Core model compilation) on every subsequent launch.
        Execute(db, $"PRAGMA user_version = {CurrentSchemaVersion};");
    }

    public static void Apply(FinoraDbContext db)
    {
        ApplyStartupCompatibility(db);
        Execute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Accounts_UpAccountId ON Accounts (UpAccountId);");
        Execute(db, "CREATE INDEX IF NOT EXISTS IX_Bills_AccountId ON Bills (AccountId);");
        Execute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_BillOccurrenceStatuses_BillId_DueDate ON BillOccurrenceStatuses (BillId, DueDate);");
        Execute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Transactions_UpTransactionId ON Transactions (UpTransactionId);");
        Execute(db, "CREATE INDEX IF NOT EXISTS IX_DebtPayments_DebtId ON DebtPayments (DebtId);");
        Execute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DebtPayments_UpTransactionId ON DebtPayments (UpTransactionId);");
        Execute(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_AppSettings_Key ON AppSettings (Key);");
    }

    public static bool ColumnExists(FinoraDbContext db, string tableName, string columnName)
    {
        var columns = GetColumns(db, tableName);
        return columns.Contains(columnName);
    }

    /// <summary>
    /// Reads all column names for a table in a single PRAGMA query.
    /// Prefer this over calling ColumnExists() per-column to avoid N connection round-trips.
    /// </summary>
    private static HashSet<string> GetColumns(FinoraDbContext db, string tableName)
    {
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        return columns;
    }

    private static void AddColumn(FinoraDbContext db, string tableName, string columnName, string definition,
        HashSet<string> existingColumns)
    {
        if (existingColumns.Contains(columnName))
            return;

        Execute(db, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};");
        existingColumns.Add(columnName); // keep the set current for any repeated calls
    }

    private static void Execute(FinoraDbContext db, string sql)
    {
        try
        {
            db.Database.ExecuteSqlRaw(sql);
        }
        catch
        {
            // Idempotent repair should keep going when a table is not present yet.
        }
    }
}
