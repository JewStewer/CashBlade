using System.Windows;
using System.Windows.Controls;

namespace Finora.Views;

public class BudgetTransferWindow : Window
{
    private readonly ComboBox _accountBox = new();

    public string SelectedAccount { get; private set; } = string.Empty;

    public BudgetTransferWindow(IEnumerable<string> accounts, string currentAccount)
    {
        Title = "Transfer To";
        Width = 360;
        Height = 180;
        MinWidth = 320;
        MinHeight = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Transfer this budget money to",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var account in accounts)
        {
            _accountBox.Items.Add(account);
        }

        _accountBox.Height = 34;
        _accountBox.SelectedItem = accounts.FirstOrDefault(a => a == currentAccount) ?? _accountBox.Items.Cast<string>().FirstOrDefault();
        panel.Children.Add(_accountBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = new Button { Content = "Save", Width = 90, Height = 34 };
        save.Click += (_, _) =>
        {
            SelectedAccount = _accountBox.SelectedItem?.ToString() ?? string.Empty;
            DialogResult = !string.IsNullOrWhiteSpace(SelectedAccount);
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        Content = panel;
    }
}
