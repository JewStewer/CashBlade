namespace Finora.Services;

public record UpBankSyncResult(
    int ImportedTransactions,
    int AppliedDebtPayments,
    int AccountBalanceAdjustments,
    int RenamedBillAdjustments,
    int AmbiguousBillMatches);
