using Finora.Data;
using Finora.Models;

namespace Finora.Services;

public static class DebtPaymentMatcher
{
    private const string ManualPaymentPrefix = "manual:";
    private const string BillPaymentPrefix = "bill:";

    public static int ApplyManualDebtPayments(FinoraDbContext db)
    {
        var applied = 0;
        var candidateTransactions = db.Transactions
            .Where(t => t.AmountCents < 0 && t.UpTransactionId == null)
            .ToList();

        foreach (var transaction in candidateTransactions)
        {
            var manualPaymentId = BuildManualPaymentId(transaction.Id);
            if (db.DebtPayments.Any(p => p.UpTransactionId == manualPaymentId))
            {
                continue;
            }

            var debt = FindMatchingDebt(db, transaction.Description);
            if (debt is null)
            {
                continue;
            }

            var paymentCents = Math.Abs(transaction.AmountCents);
            debt.BalanceCents = Math.Max(0, debt.BalanceCents - paymentCents);
            db.DebtPayments.Add(new DebtPayment
            {
                Debt = debt,
                UpTransactionId = manualPaymentId,
                AmountCents = paymentCents,
                PaidOn = transaction.Date,
                Description = transaction.Description
            });
            applied++;
        }

        return applied;
    }

    public static void ApplyBillDebtPaymentStatus(FinoraDbContext db, Bill bill, DateTime dueDate, bool isPaid)
    {
        var paymentId = BuildBillPaymentId(bill.Id, dueDate);
        var existingPayment = db.DebtPayments.FirstOrDefault(p => p.UpTransactionId == paymentId);

        if (!isPaid)
        {
            if (existingPayment is null)
            {
                return;
            }

            var existingDebt = db.Debts.FirstOrDefault(d => d.Id == existingPayment.DebtId);
            if (existingDebt is not null)
            {
                existingDebt.BalanceCents += existingPayment.AmountCents;
            }

            db.DebtPayments.Remove(existingPayment);
            return;
        }

        if (existingPayment is not null)
        {
            return;
        }

        var debt = bill.DebtId is { } debtId
            ? db.Debts.FirstOrDefault(d => d.Id == debtId)
            : FindMatchingDebt(db, bill.Name);
        if (debt is null)
        {
            return;
        }

        var paymentCents = Math.Abs(bill.AmountCents);
        debt.BalanceCents = Math.Max(0, debt.BalanceCents - paymentCents);
        db.DebtPayments.Add(new DebtPayment
        {
            Debt = debt,
            UpTransactionId = paymentId,
            AmountCents = paymentCents,
            PaidOn = dueDate.Date,
            Description = bill.Name
        });
    }

    public static Debt? FindMatchingDebt(FinoraDbContext db, string description)
    {
        var normalizedDescription = Normalize(description);
        return db.Debts
            .AsEnumerable()
            .Select(debt => new
            {
                Debt = debt,
                MatchTexts = GetDebtMatchTexts(debt)
                    .Where(matchText => Normalize(matchText) != "")
                    .ToList()
            })
            .Where(candidate => candidate.MatchTexts.Any(match => normalizedDescription.Contains(Normalize(match))))
            .OrderByDescending(candidate => candidate.MatchTexts.Max(match => Normalize(match).Length))
            .Select(candidate => candidate.Debt)
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetDebtMatchTexts(Debt debt)
    {
        var configured = (debt.UpPaymentMatchText ?? "")
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return configured.Any()
            ? configured
            : new[] { debt.Name };
    }

    private static string Normalize(string value)
    {
        return new string(value
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string BuildManualPaymentId(int transactionId)
    {
        return $"{ManualPaymentPrefix}{transactionId}";
    }

    private static string BuildBillPaymentId(int billId, DateTime dueDate)
    {
        return $"{BillPaymentPrefix}{billId}:{dueDate:yyyyMMdd}";
    }
}
