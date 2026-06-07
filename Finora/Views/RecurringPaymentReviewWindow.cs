using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;
using Finora.ViewModels;

namespace Finora.Views;

public class RecurringPaymentReviewWindow : Window
{
    private readonly CheckBox _isBillBox = new() { Content = "This recurring payment is a bill" };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _amountBox = new();
    private readonly DatePicker _dueDatePicker = new();
    private readonly ComboBox _frequencyBox = new();
    private readonly ComboBox _accountBox = new();

    public bool ShouldCreateBill => _isBillBox.IsChecked == true;

    public string BillName => _nameBox.Text.Trim();

    public decimal BillAmount { get; private set; }

    public DateTime DueDate => _dueDatePicker.SelectedDate ?? DateTime.Today;

    public BillFrequency Frequency => _frequencyBox.SelectedItem is BillFrequency frequency
        ? frequency
        : BillFrequency.Monthly;

    public int AccountId => _accountBox.SelectedValue is int accountId ? accountId : 0;

    public RecurringPaymentReviewWindow(RecurringPaymentRow recurringPayment)
    {
        Title = "Review Recurring Payment";
        Width = 460;
        Height = 520;
        MinWidth = 460;
        MinHeight = 520;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();

        LoadOptions(recurringPayment);
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "Review Recurring Payment",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _isBillBox.Margin = new Thickness(0, 0, 0, 14);
        _isBillBox.IsChecked = true;
        panel.Children.Add(_isBillBox);

        AddField(panel, "Bill name", _nameBox);
        AddField(panel, "Comes from", _accountBox);
        AddField(panel, "Amount", _amountBox);
        AddField(panel, "Due date", _dueDatePicker);
        AddField(panel, "How often", _frequencyBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var save = new Button { Content = "Confirm", Width = 90, Height = 34 };
        save.Click += Save_Click;

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
    }

    private static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        control.Height = 34;
        panel.Children.Add(control);
    }

    private void LoadOptions(RecurringPaymentRow recurringPayment)
    {
        using var db = new FinoraDbContext();

        _accountBox.ItemsSource = db.Accounts.OrderBy(a => a.Name).ToList();
        _accountBox.DisplayMemberPath = "Name";
        _accountBox.SelectedValuePath = "Id";

        _frequencyBox.ItemsSource = Enum.GetValues(typeof(BillFrequency));

        _nameBox.Text = recurringPayment.Name;
        _amountBox.Text = recurringPayment.Amount.ToString("0.00");
        _dueDatePicker.SelectedDate = recurringPayment.NextExpected;
        _frequencyBox.SelectedItem = ParseFrequency(recurringPayment.Frequency);
        SelectAccount(recurringPayment.AccountName);
    }

    private void SelectAccount(string accountName)
    {
        foreach (var item in _accountBox.Items)
        {
            if (item is Account account && string.Equals(account.Name, accountName, StringComparison.OrdinalIgnoreCase))
            {
                _accountBox.SelectedItem = account;
                return;
            }
        }

        _accountBox.SelectedIndex = _accountBox.Items.Count > 0 ? 0 : -1;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ShouldCreateBill)
        {
            DialogResult = true;
            Close();
            return;
        }

        if (string.IsNullOrWhiteSpace(BillName))
        {
            MessageBox.Show("Enter a bill name.");
            return;
        }

        if (AccountId == 0)
        {
            MessageBox.Show("Choose where the payment comes from.");
            return;
        }

        if (!decimal.TryParse(_amountBox.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Enter a positive amount.");
            return;
        }

        BillAmount = amount;
        DialogResult = true;
        Close();
    }

    private static BillFrequency ParseFrequency(string frequency)
    {
        return Enum.TryParse<BillFrequency>(frequency, out var parsed)
            ? parsed
            : BillFrequency.Monthly;
    }
}
