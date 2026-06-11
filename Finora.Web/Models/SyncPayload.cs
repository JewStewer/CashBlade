namespace Finora.Web.Models;

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
    public List<int> DeletedBillIds { get; set; } = new();
    public List<Debt> NewDebts { get; set; } = new();
    public List<Debt> UpdatedDebts { get; set; } = new();
    public List<DebtPayment> NewDebtPayments { get; set; } = new();
    public List<int> DeletedDebtPaymentIds { get; set; } = new();
    public List<Account> UpdatedAccounts { get; set; } = new();
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
