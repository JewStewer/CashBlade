namespace Finora.Models;

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
            description.StartsWith("Cover from ", StringComparison.OrdinalIgnoreCase) ||
            description.StartsWith("Cover to ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInternalMovementCategory(string? categoryName)
    {
        return categoryName is "Transfer" or "Opening Balance" or "Balance Adjustment";
    }

    public static bool IsInternalMovement(Transaction transaction)
    {
        return IsInternalMovementCategory(transaction.Category?.Name) ||
            IsInternalMovementDescription(transaction.Description) ||
            transaction.TransferId is { } transferId && transferId != Guid.Empty;
    }

    public static string GetMerchantKey(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var cleaned = description.Trim();
        var separatorIndex = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            cleaned = cleaned[..separatorIndex];
        }

        return new string(cleaned
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
