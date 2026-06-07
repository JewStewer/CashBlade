using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Cashglade", "cashglade.db");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// Check Rego's transactions
var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT COUNT(*), SUM(AmountCents) FROM Transactions WHERE AccountId = 8";
using var r = cmd.ExecuteReader();
r.Read();
var count = r.GetInt32(0);
var sum = r.IsDBNull(1) ? 0 : r.GetInt64(1);
Console.WriteLine($"Rego (Id=8): {count} transaction(s), balance = ${sum / 100m:F2}");

// Check bills
var cmd2 = conn.CreateCommand();
cmd2.CommandText = "SELECT COUNT(*) FROM Bills WHERE AccountId = 8";
var billCount = (long)(cmd2.ExecuteScalar() ?? 0L);
Console.WriteLine($"Rego bills: {billCount}");

if (count == 0 || (count <= 5 && Math.Abs(sum) <= 100))
{
    Console.WriteLine("\nSafe to delete. Removing Rego account and its transactions...");
    using var tx = conn.BeginTransaction();

    var del1 = conn.CreateCommand();
    del1.CommandText = "DELETE FROM Transactions WHERE AccountId = 8";
    del1.Transaction = tx;
    var rows = del1.ExecuteNonQuery();
    Console.WriteLine($"Deleted {rows} transaction(s).");

    var del2 = conn.CreateCommand();
    del2.CommandText = "DELETE FROM Accounts WHERE Id = 8";
    del2.Transaction = tx;
    del2.ExecuteNonQuery();
    Console.WriteLine("Deleted Rego account.");

    tx.Commit();
    Console.WriteLine("Done.");
}
else
{
    Console.WriteLine("\nAccount has significant transactions — not auto-deleting. Delete manually from the app.");
}
