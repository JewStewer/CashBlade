namespace Finora.Web.Models;

public enum CategoryType { Income, Expense }

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CategoryType Type { get; set; }
}
