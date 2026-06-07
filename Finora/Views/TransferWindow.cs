using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;

namespace Finora.Views;

public class TransferWindow : Window
{
    private readonly DatePicker _datePicker = new() { SelectedDate = DateTime.Today };
    private readonly TextBox _descriptionBox = new() { Text = "Transfer" };
    private readonly TextBox _amountBox = new();
    private readonly ComboBox _fromBox = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly ComboBox _toBox = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id" };

    public TransferWindow()
    {
        Title = "Transfer Money";
        Width = 440;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinHeight = 500;
        MinWidth = 440;
        Content = BuildContent();
        LoadAccounts();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Transfer Money", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        AddField(panel, "Date", _datePicker);
        AddField(panel, "Description", _descriptionBox);
        AddField(panel, "Amount", _amountBox);
        AddField(panel, "From account", _fromBox);
        AddField(panel, "To account", _toBox);

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

    private static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        control.Height = 32;
        panel.Children.Add(control);
    }

    private void LoadAccounts()
    {
        using var db = new FinoraDbContext();
        var accounts = db.Accounts.OrderBy(a => a.Name).ToList();
        _fromBox.ItemsSource = accounts;
        _toBox.ItemsSource = accounts.ToList();
        _fromBox.SelectedIndex = accounts.Count > 0 ? 0 : -1;
        _toBox.SelectedIndex = accounts.Count > 1 ? 1 : 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(_amountBox.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Enter a positive amount.");
            return;
        }

        if (_fromBox.SelectedValue is not int fromId || _toBox.SelectedValue is not int toId || fromId == toId)
        {
            MessageBox.Show("Choose two different accounts.");
            return;
        }

        using var db = new FinoraDbContext();
        var transferCategory = db.Categories.FirstOrDefault(c => c.Name == "Transfer")
            ?? new Category { Name = "Transfer", Type = CategoryType.Expense };
        var transferId = Guid.NewGuid();
        var date = _datePicker.SelectedDate ?? DateTime.Today;
        var description = string.IsNullOrWhiteSpace(_descriptionBox.Text) ? "Transfer" : _descriptionBox.Text.Trim();

        db.Transactions.Add(new Transaction { Date = date, Description = description, AmountDollars = -amount, AccountId = fromId, Category = transferCategory, TransferId = transferId });
        db.Transactions.Add(new Transaction { Date = date, Description = description, AmountDollars = amount, AccountId = toId, Category = transferCategory, TransferId = transferId });
        db.SaveChanges();

        DialogResult = true;
        Close();
    }
}
