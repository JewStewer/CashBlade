using System.Windows;
using Finora.Data;
using Finora.Models;
using Microsoft.EntityFrameworkCore;

namespace Finora.Views;

public partial class AddAccountWindow : Window
{
    private readonly int? _accountId;
    private decimal _currentBalance;
    private readonly Func<int, string?>? _onAddToBudget;

    public AddAccountWindow()
    {
        InitializeComponent();
        LoadOptions();
        NameBox.Focus();
    }

    public AddAccountWindow(int accountId, Func<int, string?>? onAddToBudget = null) : this()
    {
        _accountId = accountId;
        _onAddToBudget = onAddToBudget;
        Title = "Edit Account";
        HeaderText.Text = "Edit Account";
        BalanceLabel.Text = "Current balance";
        HelpText.Text = "Changing the balance creates an adjustment transaction for the difference.";
        LoadAccount(accountId);

        // Show the "Add to budget" button only when editing an existing account
        AddToBudgetButton.Visibility = System.Windows.Visibility.Visible;
    }

    private void LoadOptions()
    {
        TypeBox.ItemsSource = Enum.GetValues(typeof(AccountType));
        TypeBox.SelectedItem = AccountType.Spending;

        ColorBox.ItemsSource = new[]
        {
            new ColorOption("Teal", "#0F766E"),
            new ColorOption("Blue", "#2563EB"),
            new ColorOption("Green", "#16A34A"),
            new ColorOption("Purple", "#7C3AED"),
            new ColorOption("Red", "#DC2626"),
            new ColorOption("Orange", "#D97706"),
            new ColorOption("Slate", "#334155")
        };
        ColorBox.SelectedValue = "#0F766E";
    }

    private void LoadAccount(int accountId)
    {
        using var db = new FinoraDbContext();

        var account = db.Accounts
            .Include(a => a.Transactions)
            .FirstOrDefault(a => a.Id == accountId);

        if (account is null)
        {
            MessageBox.Show("Account not found.");
            Close();
            return;
        }

        _currentBalance = account.Transactions.Sum(t => t.AmountDollars);

        NameBox.Text = account.Name;
        TypeBox.SelectedItem = account.Type;
        BalanceBox.Text = _currentBalance.ToString("0.00");
        ColorBox.SelectedValue = account.ColorHex;
        TargetAmountBox.Text = account.TargetDollars?.ToString("0.00") ?? "";
        TargetDatePicker.SelectedDate = account.TargetDate;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter an account name.");
            return;
        }

        if (TypeBox.SelectedItem is not AccountType accountType)
        {
            MessageBox.Show("Choose an account type.");
            return;
        }

        if (!decimal.TryParse(BalanceBox.Text, out var requestedBalance))
        {
            MessageBox.Show("Enter a valid balance.");
            return;
        }

        decimal? targetAmount = null;
        if (!string.IsNullOrWhiteSpace(TargetAmountBox.Text))
        {
            if (!decimal.TryParse(TargetAmountBox.Text, out var parsedTarget) || parsedTarget <= 0)
            {
                MessageBox.Show("Enter a positive target amount, or leave it blank.");
                return;
            }

            targetAmount = parsedTarget;
        }

        using var db = new FinoraDbContext();

        var duplicateExists = db.Accounts.Any(a => EF.Functions.Like(a.Name, name) && a.Id != _accountId);

        if (duplicateExists)
        {
            MessageBox.Show("An account with this name already exists.");
            return;
        }

        if (_accountId is null)
        {
            SaveNewAccount(db, name, accountType, requestedBalance, targetAmount, TargetDatePicker.SelectedDate);
        }
        else
        {
            SaveExistingAccount(db, name, accountType, requestedBalance, targetAmount, TargetDatePicker.SelectedDate);
        }

        db.SaveChanges();

        DialogResult = true;
        Close();
    }

    private void SaveNewAccount(FinoraDbContext db, string name, AccountType accountType, decimal openingBalance, decimal? targetAmount, DateTime? targetDate)
    {
        var account = new Account
        {
            Name = name,
            Type = accountType,
            ColorHex = ColorBox.SelectedValue?.ToString() ?? "#0F766E",
            TargetDollars = targetAmount,
            TargetDate = targetDate?.Date,
            TargetStartDate = targetAmount is null ? null : DateTime.Today,
            TargetStartingBalanceDollars = targetAmount is null ? null : openingBalance
        };

        db.Accounts.Add(account);

        if (openingBalance != 0)
        {
            db.Transactions.Add(new Transaction
            {
                Date = DateTime.Today,
                Description = "Opening balance",
                AmountDollars = openingBalance,
                Account = account,
                Category = GetCategory(db, "Opening Balance"),
                TransferId = Guid.Empty
            });
        }
    }

    private void SaveExistingAccount(FinoraDbContext db, string name, AccountType accountType, decimal requestedBalance, decimal? targetAmount, DateTime? targetDate)
    {
        var account = db.Accounts
            .Include(a => a.Transactions)
            .FirstOrDefault(a => a.Id == _accountId!.Value);

        if (account is null)
        {
            MessageBox.Show("Account not found.");
            return;
        }

        account.Name = name;
        account.Type = accountType;
        account.ColorHex = ColorBox.SelectedValue?.ToString() ?? "#0F766E";

        var currentBalance = account.Transactions.Sum(t => t.AmountDollars);
        var targetChanged = account.TargetDollars != targetAmount ||
            account.TargetDate?.Date != targetDate?.Date ||
            (targetAmount is not null && account.TargetStartDate is null);

        account.TargetDollars = targetAmount;
        account.TargetDate = targetDate?.Date;
        var adjustment = requestedBalance - currentBalance;

        if (adjustment != 0)
        {
            db.Transactions.Add(new Transaction
            {
                Date = DateTime.Today,
                Description = "Balance adjustment",
                AmountDollars = adjustment,
                Account = account,
                Category = GetCategory(db, "Balance Adjustment"),
                TransferId = Guid.Empty
            });
        }

        if (targetAmount is null)
        {
            account.TargetStartDate = null;
            account.TargetStartingBalanceDollars = null;
        }
        else if (targetChanged)
        {
            account.TargetStartDate = DateTime.Today;
            account.TargetStartingBalanceDollars = requestedBalance;
        }
    }

    private static Category GetCategory(FinoraDbContext db, string name)
    {
        return db.Categories.FirstOrDefault(c => c.Name == name)
            ?? db.Categories.First(c => c.Name == "Income");
    }

    private void AddToBudget_Click(object sender, RoutedEventArgs e)
    {
        if (_accountId is null || _onAddToBudget is null)
        {
            MessageBox.Show("Save the account first, then add it to the budget.");
            return;
        }

        var error = _onAddToBudget(_accountId.Value);
        MessageBox.Show(error ?? "Target added to the budget. Check the Budget tab to see it.");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed record ColorOption(string Name, string Hex);
}
