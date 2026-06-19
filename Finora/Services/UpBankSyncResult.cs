namespace Finora.Services;

public record UpBankSyncResult(
    int ImportedTransactions,
    int AppliedDebtPayments,
    int AccountBalanceAdjustments,
    int RenamedBillAdjustments,
    int AmbiguousBillMatches);

public record UpBankOrderRepairResult(int Updated, int Skipped, int Failed);
