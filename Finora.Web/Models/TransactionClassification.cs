namespace Finora.Web.Models;

public static class TransactionClassification
{
    public static bool IsInternalMovementDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        return description.StartsWith("Transfer ", StringComparison.OrdinalIgnoreCase) ||
            description.Contains(" transfer ", StringComparison.OrdinalIgnoreCase) ||
            IsCoverMovementDescription(description);
    }

    public static bool IsCoverMovementDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var text = description.Trim();
        return ContainsPhraseWithSeparator(text, "cover from") ||
            ContainsPhraseWithSeparator(text, "cover to") ||
            ContainsPhraseWithSeparator(text, "covered from") ||
            ContainsPhraseWithSeparator(text, "covered to");
    }

    private static bool ContainsPhraseWithSeparator(string text, string phrase)
    {
        var index = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + phrase.Length;
            var afterOk = afterIndex >= text.Length || char.IsWhiteSpace(text[afterIndex]) || text[afterIndex] is ':' or '-';
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = text.IndexOf(phrase, index + phrase.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static bool IsInternalMovementCategory(string? categoryName)
    {
        return categoryName is "Transfer" or "Opening Balance" or "Balance Adjustment";
    }

    public static bool HasLinkedTransferId(Transaction transaction)
    {
        return transaction.TransferId is { } transferId && transferId != Guid.Empty;
    }
}
