using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Cashglade", "cashglade.db");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// Find the "Balance Adjustment" category id
var catCmd = conn.CreateCommand();
catCmd.CommandText = "SELECT Id FROM Categories WHERE Name = 'Balance Adjustment' LIMIT 1";
var balanceAdjustmentCategoryId = (long)(catCmd.ExecuteScalar() ?? throw new Exception("Balance Adjustment category not found"));
Console.WriteLine($"Balance Adjustment category id = {balanceAdjustmentCategoryId}");

// Recategorize the two duplicate "Nissan" reconciliation transactions in the 🚗 Car account
var upd = conn.CreateCommand();
upd.CommandText = "UPDATE Transactions SET CategoryId = $cat WHERE Id IN (991, 1089) AND Description = 'Nissan'";
upd.Parameters.AddWithValue("$cat", balanceAdjustmentCategoryId);
var rows = upd.ExecuteNonQuery();
Console.WriteLine($"Updated {rows} transaction(s).");

// Verify
Console.WriteLine("\n=== After fix ===");
var verify = conn.CreateCommand();
verify.CommandText = @"SELECT t.Id, t.Date, t.Description, t.AmountCents, c.Name FROM Transactions t
                        LEFT JOIN Categories c ON c.Id = t.CategoryId
                        WHERE t.Id IN (991, 1089)";
using (var r = verify.ExecuteReader())
{
    while (r.Read())
    {
        Console.WriteLine($"Id={r.GetInt32(0)} Date={r.GetDateTime(1):yyyy-MM-dd} Desc='{r.GetString(2)}' Amount={r.GetInt32(3) / 100m:F2} Cat='{(r.IsDBNull(4) ? "" : r.GetString(4))}'");
    }
}

// Check for other historical instances of the same "balance sync" bill-match pattern,
// to see if this bug affected other bills too (informational only, not auto-fixed).
Console.WriteLine("\n=== Other 'balance sync' bill matches (for awareness) ===");
var others = conn.CreateCommand();
others.CommandText = @"SELECT bos.Id, bos.BillId, b.Name, bos.DueDate, bos.MatchNote FROM BillOccurrenceStatuses bos
                        JOIN Bills b ON b.Id = bos.BillId
                        WHERE bos.MatchNote LIKE '%balance sync%'
                        ORDER BY bos.DueDate DESC";
using (var r = others.ExecuteReader())
{
    while (r.Read())
    {
        Console.WriteLine($"BillOccId={r.GetInt32(0)} BillId={r.GetInt32(1)} BillName='{r.GetString(2)}' DueDate={r.GetDateTime(3):yyyy-MM-dd} Note='{r.GetString(4)}'");
    }
}
