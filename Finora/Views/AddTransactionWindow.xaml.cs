using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;
using Finora.Services;

namespace Finora.Views;

public partial class AddTransactionWindow : Window
{
    private readonly int? _transactionId;

    public AddTransactionWindow()
    {
        InitializeComponent();
        LoadDropdowns();

        DatePicker.SelectedDate = DateTime.Today;
    }

    public AddTransactionWindow(int transactionId) : this()
    {
        _transactionId = transactionId;
        Title = "Edit Transaction";
        HeaderText.Text = "Edit Transaction";
        LoadTransactionForEdit(transactionId);
    }

    private void LoadDropdowns()
    {
        using var db = new FinoraDbContext();

        AccountBox.ItemsSource = db.Accounts.OrderBy(a => a.Name).ToList();
        AccountBox.SelectedIndex = 0;
        TransactionTypeBox.SelectedIndex = 0;
        LoadCategoriesForSelectedType();
    }

    private void LoadTransactionForEdit(int transactionId)
    {
        using var db = new FinoraDbContext();

        var transaction = db.Transactions.FirstOrDefault(t => t.Id == transactionId);

        if (transaction is null)
        {
            MessageBox.Show("Transaction not found.");
            Close();
            return;
        }

        DatePicker.SelectedDate = transaction.Date;
        DescriptionBox.Text = transaction.Description;
        AmountBox.Text = Math.Abs(transaction.AmountDollars).ToString("0.00");
        TransactionTypeBox.SelectedIndex = transaction.AmountDollars < 0 ? 0 : 1;

        AccountBox.SelectedValue = transaction.AccountId;
        CategoryBox.SelectedValue = transaction.CategoryId;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text, out var amount))
        {
            MessageBox.Show("Enter a valid amount.");
            return;
        }

        var selectedType =
            (TransactionTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

        if (selectedType == "Expense")
        {
            amount = -Math.Abs(amount);
        }
        else
        {
            amount = Math.Abs(amount);
        }

        if (AccountBox.SelectedValue is not int accountId)
        {
            MessageBox.Show("Choose an account.");
            return;
        }

        if (CategoryBox.SelectedValue is not int categoryId)
        {
            MessageBox.Show("Choose a category.");
            return;
        }

        using var db = new FinoraDbContext();

        if (_transactionId is null)
        {
            var transaction = new Transaction
            {
                Date = DatePicker.SelectedDate ?? DateTime.Today,
                Description = DescriptionBox.Text.Trim(),
                AmountDollars = amount,
                AccountId = accountId,
                CategoryId = categoryId,
                TransferId = null
            };

            db.Transactions.Add(transaction);
            db.SaveChanges();
            ApplyCategoryToMatchingTransactions(db, transaction.Description, transaction.AmountCents, categoryId);
        }
        else
        {
            var transaction = db.Transactions.FirstOrDefault(t => t.Id == _transactionId.Value);

            if (transaction is null)
            {
                MessageBox.Show("Transaction not found.");
                return;
            }

            transaction.Date = DatePicker.SelectedDate ?? DateTime.Today;
            transaction.Description = DescriptionBox.Text.Trim();
            transaction.AmountDollars = amount;
            transaction.AccountId = accountId;
            transaction.CategoryId = categoryId;
            ApplyCategoryToMatchingTransactions(db, transaction.Description, transaction.AmountCents, categoryId);
        }

        DebtPaymentMatcher.ApplyManualDebtPayments(db);
        db.SaveChanges();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void ApplyCategoryToMatchingTransactions(FinoraDbContext db, string description, int amountCents, int categoryId)
    {
        var merchantKey = TransactionClassification.GetMerchantKey(description);
        if (merchantKey == string.Empty)
        {
            return;
        }

        var isExpense = amountCents < 0;
        var matchingTransactions = db.Transactions
            .Where(t => isExpense ? t.AmountCents < 0 : t.AmountCents > 0)
            .ToList()
            .Where(t => TransactionClassification.GetMerchantKey(t.Description) == merchantKey)
            .ToList();

        foreach (var transaction in matchingTransactions)
        {
            transaction.CategoryId = categoryId;
        }
    }

    private void TransactionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadCategoriesForSelectedType();
    }

    private void LoadCategoriesForSelectedType()
    {
        if (CategoryBox is null || TransactionTypeBox is null)
        {
            return;
        }

        using var db = new FinoraDbContext();
        var selectedType = (TransactionTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var categoryType = selectedType == "Income" ? CategoryType.Income : CategoryType.Expense;

        CategoryBox.ItemsSource = db.Categories
            .Where(c => c.Type == categoryType)
            .OrderBy(c => c.Name)
            .ToList();

        CategoryBox.SelectedIndex = CategoryBox.Items.Count > 0 ? 0 : -1;
    }
}
