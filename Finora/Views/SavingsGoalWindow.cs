using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;

namespace Finora.Views;

public class SavingsGoalWindow : Window
{
    private readonly int? _goalId;
    private readonly TextBox _nameBox = new();
    private readonly TextBox _targetBox = new();
    private readonly TextBox _currentBox = new();
    private readonly DatePicker _targetDatePicker = new();
    private readonly ComboBox _accountBox = new();
    private readonly TextBlock _weeklyRequiredText = new();

    public SavingsGoalWindow(int? goalId = null)
    {
        _goalId = goalId;
        Title = goalId is null ? "Add Savings Goal" : "Edit Savings Goal";
        Width = 420;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinHeight = 500;
        MinWidth = 420;
        Content = BuildContent();

        if (goalId is not null)
        {
            LoadGoal(goalId.Value);
        }
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        AddField(panel, "Goal name", _nameBox);
        AddField(panel, "Current amount", _currentBox);
        AddField(panel, "Target amount", _targetBox);
        AddDateField(panel, "Target date", _targetDatePicker);
        AddAccountField(panel);

        _weeklyRequiredText.Text = "Weekly amount: enter a target amount and date";
        _weeklyRequiredText.FontWeight = FontWeights.SemiBold;
        _weeklyRequiredText.Foreground = System.Windows.Media.Brushes.DarkSlateGray;
        _weeklyRequiredText.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(_weeklyRequiredText);

        _currentBox.TextChanged += (_, _) => UpdateWeeklyRequired();
        _targetBox.TextChanged += (_, _) => UpdateWeeklyRequired();
        _targetDatePicker.SelectedDateChanged += (_, _) => UpdateWeeklyRequired();

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

    private static void AddDateField(Panel panel, string label, DatePicker picker)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        picker.Height = 32;
        panel.Children.Add(picker);
    }

    private void AddAccountField(Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Savings account target", Margin = new Thickness(0, 8, 0, 3) });
        using var db = new FinoraDbContext();
        var options = new List<AccountOption> { new(0, "No linked account") };
        options.AddRange(db.Accounts
            .Where(a => a.Type == AccountType.Savings)
            .OrderBy(a => a.Name)
            .Select(a => new AccountOption(a.Id, a.Name)));
        _accountBox.ItemsSource = options;
        _accountBox.SelectedValuePath = nameof(AccountOption.Id);
        _accountBox.DisplayMemberPath = nameof(AccountOption.Name);
        _accountBox.Height = 32;
        _accountBox.SelectedValue = options.Count == 2 ? options[1].Id : 0;
        panel.Children.Add(_accountBox);
    }

    private void LoadGoal(int id)
    {
        using var db = new FinoraDbContext();
        var goal = db.SavingsGoals.FirstOrDefault(g => g.Id == id);
        if (goal is null)
        {
            MessageBox.Show("Savings goal not found.");
            Close();
            return;
        }

        _nameBox.Text = goal.Name;
        _targetBox.Text = goal.TargetDollars.ToString("0.00");
        _currentBox.Text = goal.CurrentDollars.ToString("0.00");
        _targetDatePicker.SelectedDate = goal.TargetDate;
        SelectMatchingSavingsAccount(goal);
        UpdateWeeklyRequired();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            !decimal.TryParse(_targetBox.Text, out var target) ||
            !decimal.TryParse(_currentBox.Text, out var current))
        {
            MessageBox.Show("Enter a name and valid amounts.");
            return;
        }

        var targetDate = _targetDatePicker.SelectedDate?.Date;
        if (targetDate is null || targetDate <= DateTime.Today)
        {
            MessageBox.Show("Choose a future target date.");
            return;
        }

        var weekly = CalculateWeeklyRequired(target, current, targetDate.Value);

        using var db = new FinoraDbContext();
        var goal = _goalId is null ? new SavingsGoal() : db.SavingsGoals.FirstOrDefault(g => g.Id == _goalId.Value);
        if (goal is null)
        {
            MessageBox.Show("Savings goal not found.");
            return;
        }

        goal.Name = name;
        goal.TargetDollars = target;
        goal.CurrentDollars = current;
        goal.WeeklyContributionDollars = weekly;
        goal.TargetDate = targetDate;

        if (_goalId is null)
        {
            db.SavingsGoals.Add(goal);
        }

        ApplyLinkedAccountTarget(db, name, target, current, targetDate.Value);
        db.SaveChanges();
        DialogResult = true;
        Close();
    }

    private void SelectMatchingSavingsAccount(SavingsGoal goal)
    {
        using var db = new FinoraDbContext();
        var matchingAccount = db.Accounts
            .Where(a => a.Type == AccountType.Savings && a.TargetCents == goal.TargetCents)
            .AsEnumerable()
            .FirstOrDefault(a =>
                string.Equals(a.Name, goal.Name, StringComparison.OrdinalIgnoreCase) ||
                a.TargetDate?.Date == goal.TargetDate?.Date);

        if (matchingAccount is not null)
        {
            _accountBox.SelectedValue = matchingAccount.Id;
        }
    }

    private void ApplyLinkedAccountTarget(FinoraDbContext db, string name, decimal target, decimal current, DateTime targetDate)
    {
        if (_accountBox.SelectedValue is not int accountId || accountId <= 0)
        {
            return;
        }

        var account = db.Accounts.FirstOrDefault(a => a.Id == accountId && a.Type == AccountType.Savings);
        if (account is null)
        {
            return;
        }

        account.TargetDollars = target;
        account.TargetDate = targetDate;
        account.TargetStartDate = DateTime.Today;
        account.TargetStartingBalanceDollars = current;

        if (string.Equals(account.Name, "Savings", StringComparison.OrdinalIgnoreCase))
        {
            account.Name = name;
        }
    }

    private void UpdateWeeklyRequired()
    {
        if (!decimal.TryParse(_targetBox.Text, out var target) ||
            !decimal.TryParse(_currentBox.Text, out var current))
        {
            _weeklyRequiredText.Text = "Weekly amount: enter valid amounts";
            return;
        }

        var targetDate = _targetDatePicker.SelectedDate?.Date;
        if (targetDate is null || targetDate <= DateTime.Today)
        {
            _weeklyRequiredText.Text = "Weekly amount: choose a future target date";
            return;
        }

        var weekly = CalculateWeeklyRequired(target, current, targetDate.Value);
        _weeklyRequiredText.Text = weekly <= 0
            ? "Weekly amount: goal already funded"
            : $"Weekly amount needed: {weekly:C}";
    }

    private static decimal CalculateWeeklyRequired(decimal target, decimal current, DateTime targetDate)
    {
        var remaining = Math.Max(target - current, 0);
        if (remaining <= 0)
        {
            return 0;
        }

        var days = Math.Max((targetDate.Date - DateTime.Today).TotalDays, 1);
        var weeks = Math.Max((decimal)days / 7m, 1m);
        return Math.Ceiling((remaining / weeks) * 100m) / 100m;
    }

    private sealed record AccountOption(int Id, string Name);
}
