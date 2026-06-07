using System.Windows;
using System.Windows.Controls;
using Finora.ViewModels;
using System.Globalization;

namespace Finora.Views;

public class BudgetWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TextBox _incomeBox = new();
    private readonly TextBox _billsBox = new();
    private readonly TextBox _essentialsBox = new();
    private readonly TextBox _savingsBox = new();
    private readonly TextBox _unplannedBox = new();
    private readonly StackPanel _breakdownPanel = new();
    private readonly TextBlock _incomeNote = new()
    {
        Text = "Income is calculated from everything categorised as “Income” in the last 90 days — "
             + "including regular pay, one-off repayments, refunds, and other deposits. "
             + "If it looks too high, lower it here before saving.",
        TextWrapping = TextWrapping.Wrap,
        Foreground = System.Windows.Media.Brushes.DarkOrange,
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 8),
        Visibility = Visibility.Collapsed
    };

    public BudgetWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "Weekly Budget";
        Width = 560;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinHeight = 560;
        MinWidth = 520;

        _incomeBox.Text = viewModel.WeeklyIncome.ToString("0.00");
        _billsBox.Text = viewModel.BudgetBills.ToString("0.00");
        _essentialsBox.Text = viewModel.BudgetEssentials.ToString("0.00");
        _savingsBox.Text = viewModel.BudgetSavings.ToString("0.00");
        _unplannedBox.Text = viewModel.BudgetUnplanned.ToString("0.00");

        Content = BuildContent();
        RefreshBreakdown(viewModel.BuildSuggestedBudget());
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Weekly Budget", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });

        var suggest = new Button
        {
            Content = "Make Budget For Me",
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10)
        };
        suggest.Click += Suggest_Click;
        panel.Children.Add(suggest);

        AddField(panel, "Weekly income", _incomeBox);
        panel.Children.Add(_incomeNote);
        AddField(panel, "Bills", _billsBox);
        AddField(panel, "Essentials", _essentialsBox);
        AddField(panel, "Savings", _savingsBox);
        AddField(panel, "Unplanned", _unplannedBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Money goes toward",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 8)
        });
        panel.Children.Add(_breakdownPanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = new Button { Content = "Save", Width = 90, Height = 34 };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static void AddField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        box.Height = 32;
        panel.Children.Add(box);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(_incomeBox, out var income) ||
            !TryRead(_billsBox, out var bills) ||
            !TryRead(_essentialsBox, out var essentials) ||
            !TryRead(_savingsBox, out var savings) ||
            !TryRead(_unplannedBox, out var unplanned))
        {
            MessageBox.Show("Enter valid dollar amounts.");
            return;
        }

        _viewModel.SaveBudget(income, bills, essentials, savings, unplanned);
        DialogResult = true;
        Close();
    }

    // Called by MainWindow when opened via the "Make Budget For Me" button so the
    // suggestion is already loaded and the user reviews before committing.
    public void PreloadSuggestion() => Suggest_Click(this, new RoutedEventArgs());

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        var suggestion = _viewModel.BuildSuggestedBudget();
        _incomeBox.Text = suggestion.WeeklyIncome.ToString("0.00");
        _billsBox.Text = suggestion.Bills.ToString("0.00");
        _essentialsBox.Text = suggestion.Essentials.ToString("0.00");
        _savingsBox.Text = suggestion.Savings.ToString("0.00");
        _unplannedBox.Text = suggestion.Unplanned.ToString("0.00");
        RefreshBreakdown(suggestion);
        _incomeNote.Visibility = Visibility.Visible;
    }

    private void RefreshBreakdown(BudgetSuggestion suggestion)
    {
        _breakdownPanel.Children.Clear();

        foreach (var bucket in suggestion.Breakdown.Where(r => r.IsIncluded).GroupBy(r => r.Bucket))
        {
            var bucketTotal = bucket.Sum(r => r.Amount);
            _breakdownPanel.Children.Add(new TextBlock
            {
                Text = $"{bucket.Key}: {bucketTotal:C}",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 3)
            });

            foreach (var row in bucket)
            {
                _breakdownPanel.Children.Add(new TextBlock
                {
                    Text = $"{row.Name}: {row.Amount:C}/week - {row.Detail}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 2, 0, 2)
                });
            }
        }
    }

    private static bool TryRead(TextBox box, out decimal value)
    {
        var text = box.Text.Trim();
        return (decimal.TryParse(text, NumberStyles.Currency, CultureInfo.CurrentCulture, out value) ||
                decimal.TryParse(text, NumberStyles.Currency, CultureInfo.InvariantCulture, out value)) &&
               value >= 0;
    }
}
