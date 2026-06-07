using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;

namespace Finora.Views;

public class DebtWindow : Window
{
    private readonly int? _debtId;
    private readonly TextBox _nameBox = new();
    private readonly TextBox _balanceBox = new();
    private readonly TextBox _minimumBox = new();
    private readonly ComboBox _paymentPeriodBox = new();
    private readonly TextBox _interestBox = new();
    private readonly TextBox _originalBox = new();
    private readonly TextBox _upMatchBox = new();

    public DebtWindow(int? debtId = null)
    {
        _debtId = debtId;
        Title = debtId is null ? "Add Debt" : "Edit Debt";
        Width = 420;
        Height = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinHeight = 590;
        MinWidth = 420;
        Content = BuildContent();

        if (debtId is not null)
        {
            LoadDebt(debtId.Value);
        }
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        AddField(panel, "Loan name", _nameBox);
        AddField(panel, "Balance owing", _balanceBox);
        AddField(panel, "Minimum payment", _minimumBox);
        AddPaymentPeriodField(panel);
        AddField(panel, "Interest rate", _interestBox);
        AddField(panel, "Original balance", _originalBox);
        AddField(panel, "Up payment match text", _upMatchBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = new Button { Content = "Save", Width = 90, Height = 34 };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        return panel;
    }

    private static void AddField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        box.Height = 32;
        panel.Children.Add(box);
    }

    private void AddPaymentPeriodField(Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Payment period", Margin = new Thickness(0, 8, 0, 3) });
        _paymentPeriodBox.Height = 32;
        _paymentPeriodBox.ItemsSource = new[] { "Weekly", "Fortnightly", "Monthly" };
        _paymentPeriodBox.SelectedItem = "Weekly";
        panel.Children.Add(_paymentPeriodBox);
    }

    private void LoadDebt(int id)
    {
        using var db = new FinoraDbContext();
        var debt = db.Debts.FirstOrDefault(d => d.Id == id);
        if (debt is null)
        {
            MessageBox.Show("Debt not found.");
            Close();
            return;
        }

        _nameBox.Text = debt.Name;
        _balanceBox.Text = debt.BalanceDollars.ToString("0.00");
        _minimumBox.Text = debt.MinimumPaymentDollars.ToString("0.00");
        _paymentPeriodBox.SelectedItem = NormalizePaymentPeriod(debt.PaymentPeriod);
        _interestBox.Text = debt.InterestRate?.ToString("0.##") ?? "";
        _originalBox.Text = debt.OriginalBalanceDollars.ToString("0.00");
        _upMatchBox.Text = debt.UpPaymentMatchText ?? "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            !decimal.TryParse(_balanceBox.Text, out var balance) ||
            !decimal.TryParse(_minimumBox.Text, out var minimum) ||
            !decimal.TryParse(_originalBox.Text, out var original))
        {
            MessageBox.Show("Enter a name and valid amounts.");
            return;
        }

        decimal? interest = null;
        if (!string.IsNullOrWhiteSpace(_interestBox.Text) && decimal.TryParse(_interestBox.Text, out var parsedInterest))
        {
            interest = parsedInterest;
        }

        using var db = new FinoraDbContext();
        var debt = _debtId is null ? new Debt() : db.Debts.FirstOrDefault(d => d.Id == _debtId.Value);
        if (debt is null)
        {
            MessageBox.Show("Debt not found.");
            return;
        }

        debt.Name = name;
        debt.BalanceDollars = balance;
        debt.MinimumPaymentDollars = minimum;
        debt.PaymentPeriod = NormalizePaymentPeriod(_paymentPeriodBox.SelectedItem as string);
        debt.InterestRate = interest;
        debt.OriginalBalanceDollars = original <= 0 ? balance : original;
        debt.UpPaymentMatchText = string.IsNullOrWhiteSpace(_upMatchBox.Text)
            ? null
            : _upMatchBox.Text.Trim();

        if (_debtId is null)
        {
            db.Debts.Add(debt);
        }

        db.SaveChanges();
        DialogResult = true;
        Close();
    }

    private static string NormalizePaymentPeriod(string? paymentPeriod)
    {
        return paymentPeriod is "Weekly" or "Fortnightly" or "Monthly"
            ? paymentPeriod
            : "Weekly";
    }
}
