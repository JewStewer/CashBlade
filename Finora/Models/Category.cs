using System.Transactions;

namespace Finora.Models;

public enum CategoryType
{
    Income,
    Expense
}

public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public CategoryType Type { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}