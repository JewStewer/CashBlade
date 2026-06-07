using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;
using Finora.ViewModels;

namespace Finora.Views;

public class WhatIfBudgetWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _viewModel;
    private readonly ObservableCollection<WhatIfBudgetItem> _items = new();
    private readonly ComboBox _categoryBox = new();
    private readonly ComboBox _transferBox = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBox _amountBox = new();
    private readonly TextBox _incomeBox = new();
    private readonly TextBox _scenarioNameBox = new();
    private readonly ComboBox _scenarioBox = new();
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _bucketTotalsText = new();
    private readonly TextBlock _duplicateText = new();
    private readonly DataGrid _itemsGrid = new();

    private decimal _weeklyIncome;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WhatIfBudgetWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "What-if budget";
        Width = 920;
        Height = 680;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _weeklyIncome = viewModel.WeeklyIncome;
        foreach (var row in viewModel.BuildSuggestedBudget().Breakdown.Where(r => r.IsIncluded).OrderBy(r => r.Bucket).ThenBy(r => r.Name))
        {
            _items.Add(new WhatIfBudgetItem
            {
                Category = row.Bucket,
                Name = row.Name,
                TransferTo = row.TransferTo,
                WeeklyAmount = row.Amount
            });
        }

        foreach (var category in viewModel.BudgetCategoryOptions)
        {
            _categoryBox.Items.Add(category);
        }

        foreach (var account in viewModel.GetBudgetTransferAccounts())
        {
            _transferBox.Items.Add(account);
        }

        _categoryBox.SelectedItem = "Unplanned";
        _transferBox.SelectedIndex = _transferBox.Items.Count > 0 ? 0 : -1;
        Content = BuildContent();
        LoadScenarioNames();
        RefreshSummary();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "What-if budget",
            FontSize = 22,
            FontWeight = FontWeights.Bold
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Try changes here without changing your real budget.",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 3, 0, 0)
        });
        header.Children.Add(titlePanel);

        _summaryText.FontWeight = FontWeights.SemiBold;
        _summaryText.FontSize = 16;
        _summaryText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_summaryText, 1);
        header.Children.Add(_summaryText);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var addPanel = BuildAddPanel();
        DockPanel.SetDock(addPanel, Dock.Top);
        root.Children.Add(addPanel);

        var scenarioPanel = BuildScenarioPanel();
        DockPanel.SetDock(scenarioPanel, Dock.Top);
        root.Children.Add(scenarioPanel);

        _itemsGrid.ItemsSource = _items;
        _itemsGrid.AutoGenerateColumns = false;
        _itemsGrid.SelectionMode = DataGridSelectionMode.Extended;
        _itemsGrid.CanUserAddRows = false;
        _itemsGrid.Margin = new Thickness(0, 12, 0, 0);
        _itemsGrid.CurrentCellChanged += (_, _) => RefreshSummary();
        _itemsGrid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new System.Windows.Data.Binding(nameof(WhatIfBudgetItem.Category)), Width = 120 });
        _itemsGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding(nameof(WhatIfBudgetItem.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _itemsGrid.Columns.Add(new DataGridTextColumn { Header = "Transfer to", Binding = new System.Windows.Data.Binding(nameof(WhatIfBudgetItem.TransferTo)), Width = 160 });
        _itemsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Per week",
            Binding = new System.Windows.Data.Binding(nameof(WhatIfBudgetItem.WeeklyAmount)) { StringFormat = "C" },
            Width = 110
        });

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var footerText = new StackPanel();
        footerText.Children.Add(_bucketTotalsText);
        footerText.Children.Add(_duplicateText);
        _duplicateText.Foreground = System.Windows.Media.Brushes.IndianRed;
        _duplicateText.Margin = new Thickness(0, 4, 0, 0);
        footer.Children.Add(footerText);

        var apply = new Button
        {
            Content = "Apply to real budget",
            Width = 150,
            Height = 34
        };
        apply.Click += Apply_Click;
        Grid.SetColumn(apply, 1);
        footer.Children.Add(apply);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_itemsGrid);

        return root;
    }

    private UIElement BuildScenarioPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };

        _scenarioNameBox.Width = 180;
        _scenarioNameBox.Margin = new Thickness(0, 0, 8, 0);
        _scenarioNameBox.ToolTip = "Scenario name";
        _scenarioBox.Width = 180;
        _scenarioBox.Margin = new Thickness(0, 0, 8, 0);

        var save = new Button { Content = "Save scenario", Width = 112, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += SaveScenario_Click;
        var load = new Button { Content = "Load", Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        load.Click += LoadScenario_Click;
        var delete = new Button { Content = "Delete", Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        delete.Click += DeleteScenario_Click;
        var reset = new Button { Content = "Reset to current", Width = 118, Height = 32 };
        reset.Click += (_, _) => ResetToCurrentBudget();

        panel.Children.Add(_scenarioNameBox);
        panel.Children.Add(_scenarioBox);
        panel.Children.Add(save);
        panel.Children.Add(load);
        panel.Children.Add(delete);
        panel.Children.Add(reset);
        return panel;
    }

    private UIElement BuildAddPanel()
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(panel, "Weekly income", 0);
        AddLabel(panel, "Name", 1);
        AddLabel(panel, "Per week", 2);
        AddLabel(panel, "Category", 3);
        AddLabel(panel, "Transfer to", 4);

        _incomeBox.Text = _weeklyIncome.ToString("0.00");
        _incomeBox.Margin = new Thickness(0, 0, 8, 0);
        _incomeBox.TextChanged += (_, _) =>
        {
            if (TryReadAmount(_incomeBox.Text, out var income))
            {
                _weeklyIncome = income;
                RefreshSummary();
            }
        };
        Grid.SetRow(_incomeBox, 2);
        panel.Children.Add(_incomeBox);

        _nameBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetRow(_nameBox, 2);
        Grid.SetColumn(_nameBox, 1);
        panel.Children.Add(_nameBox);

        _amountBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetRow(_amountBox, 2);
        Grid.SetColumn(_amountBox, 2);
        panel.Children.Add(_amountBox);

        _categoryBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetRow(_categoryBox, 2);
        Grid.SetColumn(_categoryBox, 3);
        panel.Children.Add(_categoryBox);

        _transferBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetRow(_transferBox, 2);
        Grid.SetColumn(_transferBox, 4);
        panel.Children.Add(_transferBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = "Add", Width = 76, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += Add_Click;
        var remove = new Button { Content = "Remove", Width = 86, Height = 32 };
        remove.Click += Remove_Click;
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        Grid.SetRow(buttons, 2);
        Grid.SetColumn(buttons, 5);
        panel.Children.Add(buttons);

        return panel;
    }

    private static void AddLabel(Grid panel, string text, int column)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(label, column);
        panel.Children.Add(label);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !TryReadAmount(_amountBox.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Enter a name and a weekly amount greater than $0.");
            return;
        }

        _items.Add(new WhatIfBudgetItem
        {
            Category = _categoryBox.SelectedItem?.ToString() ?? "Unplanned",
            Name = name,
            WeeklyAmount = amount,
            TransferTo = _transferBox.SelectedItem?.ToString() ?? string.Empty
        });

        _nameBox.Clear();
        _amountBox.Clear();
        RefreshSummary();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = _itemsGrid.SelectedItems.Cast<WhatIfBudgetItem>().ToList();
        foreach (var item in selected)
        {
            _items.Remove(item);
        }

        RefreshSummary();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Replace your real weekly budget totals with this what-if total?",
            "Apply what-if budget",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var bills = SumCategory("Bills");
        var essentials = SumCategory("Essentials");
        // Only count items explicitly set to "Savings" category.
        // Account-target items (Category = account name like "Phone") are already
        // counted inside the Bills allocation via the funding plan — don't double-count.
        var savings = _items
            .Where(i => string.Equals(i.Category, "Savings", StringComparison.OrdinalIgnoreCase))
            .Sum(i => Math.Max(i.WeeklyAmount, 0));
        var unplanned = SumCategory("Unplanned");

        // Save aggregate totals
        _viewModel.SaveBudget(_weeklyIncome, bills, essentials, savings, unplanned);

        // Save individual custom items so they appear as line items in the budget
        _viewModel.ApplyWhatIfCustomItems(
            _items.Select(i => (i.Category, i.Name, i.WeeklyAmount, i.TransferTo)));

        DialogResult = true;
        Close();
    }

    private void SaveScenario_Click(object sender, RoutedEventArgs e)
    {
        var name = _scenarioNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a scenario name.");
            return;
        }

        using var db = new FinoraDbContext();
        SaveScenario(db, name, SerializeScenario());
        db.SaveChanges();
        LoadScenarioNames();
        _scenarioBox.SelectedItem = name;
    }

    private void LoadScenario_Click(object sender, RoutedEventArgs e)
    {
        var name = _scenarioBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        using var db = new FinoraDbContext();
        var value = db.AppSettings
            .Where(s => s.Key == BuildScenarioKey(name))
            .Select(s => s.Value)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        DeserializeScenario(value);
        _scenarioNameBox.Text = name;
        RefreshSummary();
    }

    private void DeleteScenario_Click(object sender, RoutedEventArgs e)
    {
        var name = _scenarioBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        using var db = new FinoraDbContext();
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == BuildScenarioKey(name));
        if (setting is not null)
        {
            db.AppSettings.Remove(setting);
            db.SaveChanges();
        }

        LoadScenarioNames();
    }

    private void ResetToCurrentBudget()
    {
        _items.Clear();
        _weeklyIncome = _viewModel.WeeklyIncome;
        _incomeBox.Text = _weeklyIncome.ToString("0.00");
        foreach (var row in _viewModel.BuildSuggestedBudget().Breakdown.Where(r => r.IsIncluded).OrderBy(r => r.Bucket).ThenBy(r => r.Name))
        {
            _items.Add(new WhatIfBudgetItem
            {
                Category = row.Bucket,
                Name = row.Name,
                TransferTo = row.TransferTo,
                WeeklyAmount = row.Amount
            });
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var total = _items.Sum(i => Math.Max(i.WeeklyAmount, 0));
        var left = _weeklyIncome - total;
        _summaryText.Text = $"Total {total:C}/week | Left {left:C}/week";
        _summaryText.Foreground = left < 0
            ? System.Windows.Media.Brushes.IndianRed
            : System.Windows.Media.Brushes.MediumAquamarine;

        _bucketTotalsText.Text =
            $"Bills {SumCategory("Bills"):C} | Essentials {SumCategory("Essentials"):C} | Savings {SumCategory("Savings"):C} | Unplanned {SumCategory("Unplanned"):C}";

        var duplicateCount = _items
            .GroupBy(i => $"{GetBudgetCategory(i.Category)}::{i.Name.Trim()}", StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);
        _duplicateText.Text = duplicateCount == 0
            ? string.Empty
            : $"{duplicateCount} possible duplicate item{(duplicateCount == 1 ? "" : "s")}.";
    }

    private decimal SumCategory(string category)
    {
        return _items
            .Where(i => GetBudgetCategory(i.Category) == category)
            .Sum(i => Math.Max(i.WeeklyAmount, 0));
    }

    private static string GetBudgetCategory(string category)
    {
        return category is "Bills" or "Essentials" or "Unplanned"
            ? category
            : "Savings";
    }

    private static bool TryReadAmount(string text, out decimal amount)
    {
        return decimal.TryParse(text, NumberStyles.Currency, CultureInfo.CurrentCulture, out amount) ||
            decimal.TryParse(text, NumberStyles.Currency, CultureInfo.InvariantCulture, out amount);
    }

    private void LoadScenarioNames()
    {
        using var db = new FinoraDbContext();
        var selected = _scenarioBox.SelectedItem?.ToString();
        _scenarioBox.Items.Clear();
        foreach (var name in db.AppSettings
            .Where(s => s.Key.StartsWith("WhatIfScenario::"))
            .Select(s => s.Key.Substring("WhatIfScenario::".Length))
            .OrderBy(s => s)
            .ToList())
        {
            _scenarioBox.Items.Add(name);
        }

        _scenarioBox.SelectedItem = selected is not null && _scenarioBox.Items.Contains(selected)
            ? selected
            : _scenarioBox.Items.Cast<string>().FirstOrDefault();
    }

    private string SerializeScenario()
    {
        var itemText = string.Join('\n', _items.Select(item => string.Join('\t',
            Encode(item.Category),
            Encode(item.Name),
            Encode(item.TransferTo),
            item.WeeklyAmount.ToString(CultureInfo.InvariantCulture))));
        return $"{_weeklyIncome.ToString(CultureInfo.InvariantCulture)}\r\n{itemText}";
    }

    private void DeserializeScenario(string value)
    {
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length > 0 && decimal.TryParse(lines[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var income))
        {
            _weeklyIncome = income;
            _incomeBox.Text = _weeklyIncome.ToString("0.00");
        }

        _items.Clear();
        foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var parts = line.Split('\t');
            if (parts.Length != 4 || !decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            _items.Add(new WhatIfBudgetItem
            {
                Category = Decode(parts[0]),
                Name = Decode(parts[1]),
                TransferTo = Decode(parts[2]),
                WeeklyAmount = amount
            });
        }
    }

    private static void SaveScenario(FinoraDbContext db, string name, string value)
    {
        var key = BuildScenarioKey(name);
        var setting = db.AppSettings.FirstOrDefault(s => s.Key == key);
        if (setting is null)
        {
            setting = new AppSetting { Key = key };
            db.AppSettings.Add(setting);
        }

        setting.Value = value;
    }

    private static string BuildScenarioKey(string name) => $"WhatIfScenario::{name.Trim()}";

    private static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

    private static string Decode(string value)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class WhatIfBudgetItem
    {
        public string Category { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string TransferTo { get; set; } = string.Empty;

        public decimal WeeklyAmount { get; set; }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
