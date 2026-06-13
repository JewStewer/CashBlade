using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Finora.Data;
using Finora.Models;
using Finora.Services;
using Finora.ViewModels;
using Finora.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text;

namespace Finora;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly UpBankSyncService _upBankSyncService = new();
    private readonly DispatcherTimer _upBankSyncTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly DispatcherTimer _startupWorkTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _isLoadingPayday;
    private bool _isSyncingUpBank;

    public MainWindow()
    {
        LogStartup("MainWindow constructor entered.");
        LogStartup("InitializeComponent started.");
        InitializeComponent();
        LogStartup("InitializeComponent finished.");

        LogStartup("MainViewModel creation started.");
        ViewModel = new MainViewModel();
        LogStartup("MainViewModel creation finished.");
        LogStartup("DataContext assignment started.");
        DataContext = ViewModel;
        LogStartup("DataContext assignment finished.");

        _isLoadingPayday = true;
        NextPaydayPicker.SelectedDate = ViewModel.NextPayDate;
        _isLoadingPayday = false;

        Loaded += MainWindow_Loaded;
        _upBankSyncTimer.Tick += async (_, _) => await SyncUpBankAsync(showSuccessMessage: false);
        _startupWorkTimer.Tick += StartupWorkTimer_Tick;
        LogStartup("MainWindow constructor finished.");
    }

    private static void LogStartup(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cashglade");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup.log"),
                $"[{DateTimeOffset.Now:O}] MainWindow: {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void AddTransaction_Click(object sender, RoutedEventArgs e) => ShowDialogAndRefresh(new AddTransactionWindow());

    private void Transfer_Click(object sender, RoutedEventArgs e) => ShowDialogAndRefresh(new TransferWindow());

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        new DiagnosticsWindow { Owner = this }.ShowDialog();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_upBankSyncService.HasAccessToken())
        {
            _upBankSyncTimer.Start();
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            LogStartup("Deferred dashboard load started.");
            ViewModel.LoadDashboard();
            LogStartup("Deferred dashboard load finished.");
            _isLoadingPayday = true;
            NextPaydayPicker.SelectedDate = ViewModel.NextPayDate;
            _isLoadingPayday = false;
            _startupWorkTimer.Start();
        }), DispatcherPriority.Background);
    }

    private async void StartupWorkTimer_Tick(object? sender, EventArgs e)
    {
        _startupWorkTimer.Stop();
        // Run the heavy transaction scan on a background thread so it doesn't freeze the UI.
        await Task.Run(() => ViewModel.LoadRecurringPayments());
        ViewModel.LoadReports(refreshRecurring: false);
        ViewModel.LoadInsights();
        await ViewModel.LoadDebtStrategiesAsync();

        if (_upBankSyncService.HasAccessToken())
        {
            await SyncUpBankAsync(showSuccessMessage: false);
        }
    }

    private void UpBankSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new UpBankSettingsWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            if (_upBankSyncService.HasAccessToken())
            {
                _upBankSyncTimer.Start();
            }
            else
            {
                _upBankSyncTimer.Stop();
            }
        }
    }

    private async void SyncUpBank_Click(object sender, RoutedEventArgs e)
    {
        await SyncUpBankAsync(showSuccessMessage: true);
    }

    private async Task SyncUpBankAsync(bool showSuccessMessage)
    {
        if (_isSyncingUpBank)
        {
            return;
        }

        _isSyncingUpBank = true;
        try
        {
            var result = await _upBankSyncService.SyncAsync();
            ViewModel.LoadDashboard();

            // New transactions can change Reports/Insights too — refresh them so an
            // automatic background sync is actually reflected wherever the user is
            // looking, not just on the Dashboard.
            if (result.ImportedTransactions > 0 || result.AppliedDebtPayments > 0
                || result.AccountBalanceAdjustments > 0 || result.RenamedBillAdjustments > 0)
            {
                ViewModel.LoadReports(refreshRecurring: false);
                ViewModel.LoadInsights();
            }

            if (showSuccessMessage)
            {
                MessageBox.Show($"Imported {result.ImportedTransactions} Up transactions, applied {result.AppliedDebtPayments} debt payments, updated {result.AccountBalanceAdjustments} account balances, renamed {result.RenamedBillAdjustments} bill adjustments, and found {result.AmbiguousBillMatches} ambiguous bill match{(result.AmbiguousBillMatches == 1 ? "" : "es")}.");
            }
        }
        catch (InvalidOperationException ex)
        {
            if (showSuccessMessage)
            {
                MessageBox.Show(ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (showSuccessMessage)
            {
                MessageBox.Show($"Up Bank sync failed: {GetExceptionMessage(ex)}");
            }
        }
        finally
        {
            _isSyncingUpBank = false;
        }
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e) => ShowDialogAndRefresh(new AddAccountWindow());

    private static string GetExceptionMessage(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(Environment.NewLine, messages.Distinct());
    }

    private void EditAccount_Click(object sender, RoutedEventArgs e) => EditSelectedAccount();

    private void AccountsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedAccount();

    private void EditSelectedAccount()
    {
        if (ViewModel.SelectedAccount is null)
        {
            MessageBox.Show("Choose an account to edit.");
            return;
        }

        ShowDialogAndRefresh(new AddAccountWindow(ViewModel.SelectedAccount.Id, AddToBudgetCallback));
    }

    private void DashboardAccountCard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: AccountRow account })
            {
                ShowDialogAndRefresh(new AddAccountWindow(account.Id, AddToBudgetCallback));
                return;
            }
            element = VisualTreeHelper.GetParent(element);
        }
    }

    private string? AddToBudgetCallback(int accountId)
    {
        var error = ViewModel.AddAccountTargetToBudget(accountId);
        if (error is null)
        {
            ViewModel.SaveSuggestedBudget();
            ViewModel.LoadDashboard();
        }

        return error;
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedAccounts();
    }

    private void AddAccountTargetToBudget_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            MessageBox.Show("Choose a savings account to add to the budget.");
            return;
        }

        var error = ViewModel.AddAccountTargetToBudget(ViewModel.SelectedAccount.Id);
        if (error is not null)
        {
            MessageBox.Show(error);
            return;
        }

        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
        MessageBox.Show($"\"{ViewModel.SelectedAccount.Name}\" target added to the budget. Check the Budget tab to see it.");
    }

    private void DeleteSelectedAccounts()
    {
        var selectedAccounts = AccountsList.SelectedItems.OfType<AccountRow>().ToList();
        if (selectedAccounts.Count == 0 && ViewModel.SelectedAccount is not null)
        {
            selectedAccounts.Add(ViewModel.SelectedAccount);
        }

        if (selectedAccounts.Count == 0)
        {
            MessageBox.Show("Choose an account to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        var ids = selectedAccounts.Select(a => a.Id).ToList();
        var blockedNames = db.Accounts
            .Where(a => ids.Contains(a.Id))
            .Where(a => db.Transactions.Any(t => t.AccountId == a.Id && t.Description != "Up balance adjustment"))
            .Select(a => a.Name)
            .ToList();

        if (blockedNames.Count > 0)
        {
            MessageBox.Show($"These accounts have transactions and were not deleted: {string.Join(", ", blockedNames)}");
            return;
        }

        var accounts = db.Accounts.Where(a => ids.Contains(a.Id)).ToList();
        if (accounts.Count == 0)
        {
            MessageBox.Show("Account not found.");
            return;
        }

        var removableAdjustmentTransactions = db.Transactions
            .Where(t => ids.Contains(t.AccountId) && t.Description == "Up balance adjustment")
            .ToList();
        var removableBillIds = db.Bills
            .Where(b => ids.Contains(b.AccountId))
            .Select(b => b.Id)
            .ToList();
        var removableBillStatuses = db.BillOccurrenceStatuses
            .Where(s => removableBillIds.Contains(s.BillId))
            .ToList();
        var removableBills = db.Bills
            .Where(b => ids.Contains(b.AccountId))
            .ToList();

        db.BillOccurrenceStatuses.RemoveRange(removableBillStatuses);
        db.Bills.RemoveRange(removableBills);
        db.Transactions.RemoveRange(removableAdjustmentTransactions);
        db.Accounts.RemoveRange(accounts);
        db.SaveChanges();
        ViewModel.LoadDashboard();
    }

    private void AddBill_Click(object sender, RoutedEventArgs e) => ShowDialogAndRefresh(new BillWindow(ViewModel.NextPayDate));

    private void EditBill_Click(object sender, RoutedEventArgs e) => EditSelectedBill();

    private void BillsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedBill();

    private void EditSelectedBill()
    {
        if (ViewModel.SelectedBill is null)
        {
            MessageBox.Show("Choose a bill to edit.");
            return;
        }

        ShowDialogAndRefresh(new BillWindow(ViewModel.SelectedBill.Id, ViewModel.NextPayDate));
    }

    private void MarkBillsPaid_Click(object sender, RoutedEventArgs e) => SetSelectedBillsPaid(true);

    private void MarkBillsUnpaid_Click(object sender, RoutedEventArgs e) => SetSelectedBillsPaid(false);

    private void BillPaidCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not BillRow bill)
        {
            return;
        }

        bill.IsPaid = checkBox.IsChecked == true;
        SetBillPaid(bill, checkBox.IsChecked == true);
        e.Handled = true;
    }

    private void AddBillToBudget_Click(object sender, RoutedEventArgs e)
    {
        var selectedBills = BillsGrid.SelectedItems.OfType<BillRow>().ToList();
        if (selectedBills.Count == 0 && ViewModel.SelectedBill is not null)
        {
            selectedBills.Add(ViewModel.SelectedBill);
        }

        if (selectedBills.Count == 0)
        {
            MessageBox.Show("Choose a bill to add to the budget.");
            return;
        }

        var added = 0;
        foreach (var bill in selectedBills)
        {
            if (ViewModel.AddBillToBudget(bill.Id))
            {
                added++;
            }
        }

        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
    }

    private void SetSelectedBillsPaid(bool isPaid)
    {
        var selectedBills = BillsGrid.SelectedItems.OfType<BillRow>().ToList();
        if (selectedBills.Count == 0 && ViewModel.SelectedBill is not null)
        {
            selectedBills.Add(ViewModel.SelectedBill);
        }

        if (selectedBills.Count == 0)
        {
            MessageBox.Show("Choose one or more bills.");
            return;
        }

        SetBillsPaid(selectedBills, isPaid);
    }

    private void SetBillPaid(BillRow bill, bool isPaid)
    {
        SetBillsPaid(new[] { bill }, isPaid);
    }

    private void SetBillsPaid(IEnumerable<BillRow> selectedBills, bool isPaid)
    {
        var selectedBillList = selectedBills.ToList();

        using var db = new FinoraDbContext();
        var billIds = selectedBillList.Select(b => b.Id).Distinct().ToList();
        var existingBills = db.Bills.Where(b => billIds.Contains(b.Id)).ToDictionary(b => b.Id);
        if (existingBills.Count == 0)
        {
            MessageBox.Show("Bills not found.");
            return;
        }

        foreach (var selectedBill in selectedBillList.Where(b => existingBills.ContainsKey(b.Id)))
        {
            var dueDate = selectedBill.DueDate.Date;
            var dbBill  = existingBills[selectedBill.Id];
            var status  = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == selectedBill.Id && s.DueDate == dueDate);
            if (status is null)
            {
                status = new Finora.Models.BillOccurrenceStatus
                {
                    BillId = selectedBill.Id,
                    DueDate = dueDate
                };
                db.BillOccurrenceStatuses.Add(status);
            }

            status.IsPaid = isPaid;
            status.PaidOn = isPaid ? DateTime.Today : null;
            status.MatchNote = isPaid ? "Marked paid manually" : string.Empty;
            if (!isPaid)
            {
                status.MatchedTransactionId = null;
            }

            // Auto-advance: when marking paid, move the bill's base DueDate to the next
            // occurrence so the editor and bill list always show the upcoming date.
            if (isPaid && dueDate >= dbBill.DueDate.Date)
            {
                dbBill.DueDate = Finora.ViewModels.MainViewModel.GetNextBillDueDate(dueDate, dbBill.Frequency);
            }

            DebtPaymentMatcher.ApplyBillDebtPaymentStatus(db, dbBill, dueDate, isPaid);
        }

        db.SaveChanges();
        ViewModel.RefreshAfterBillPaymentChange();
    }

    private void DeleteBill_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedBillSeries();
    }

    private void DeleteBillSeries_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedBillSeries();
    }

    private void PreviousBillCalendarMonth_Click(object sender, RoutedEventArgs e) => ViewModel.PreviousBillCalendarMonth();

    private void CurrentBillCalendarMonth_Click(object sender, RoutedEventArgs e) => ViewModel.CurrentBillCalendarMonth();

    private void NextBillCalendarMonth_Click(object sender, RoutedEventArgs e) => ViewModel.NextBillCalendarMonth();

    private void CalendarBillMarkPaid_Click(object sender, RoutedEventArgs e) => SetCalendarBillPaid(sender, true);

    private void CalendarBillMarkUnpaid_Click(object sender, RoutedEventArgs e) => SetCalendarBillPaid(sender, false);

    private void SetCalendarBillPaid(object sender, bool isPaid)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BillCalendarBillRow bill)
        {
            return;
        }

        using var db = new FinoraDbContext();
        var existingBill = db.Bills.FirstOrDefault(b => b.Id == bill.BillId);
        if (existingBill is null)
        {
            MessageBox.Show("Bill not found.");
            return;
        }

        var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == bill.BillId && s.DueDate == bill.DueDate.Date);
        if (status is null)
        {
            status = new Finora.Models.BillOccurrenceStatus
            {
                BillId = bill.BillId,
                DueDate = bill.DueDate.Date
            };
            db.BillOccurrenceStatuses.Add(status);
        }

        status.IsPaid = isPaid;
        status.PaidOn = isPaid ? DateTime.Today : null;
        status.MatchNote = isPaid ? "Marked paid manually" : string.Empty;
        if (!isPaid)
        {
            status.MatchedTransactionId = null;
        }

        // Auto-advance: move base DueDate to next occurrence when marking paid.
        if (isPaid && bill.DueDate.Date >= existingBill.DueDate.Date)
        {
            existingBill.DueDate = Finora.ViewModels.MainViewModel.GetNextBillDueDate(bill.DueDate.Date, existingBill.Frequency);
        }

        DebtPaymentMatcher.ApplyBillDebtPaymentStatus(db, existingBill, bill.DueDate.Date, isPaid);
        db.SaveChanges();
        ViewModel.RefreshAfterBillPaymentChange();
    }

    private void DeleteSelectedBills()
    {
        var selectedBills = BillsGrid.SelectedItems.OfType<BillRow>().ToList();
        if (selectedBills.Count == 0 && ViewModel.SelectedBill is not null)
        {
            selectedBills.Add(ViewModel.SelectedBill);
        }

        if (selectedBills.Count == 0)
        {
            MessageBox.Show("Choose a bill to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        var ids = selectedBills.Select(b => b.Id).Distinct().ToList();
        var existingBills = db.Bills.Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToHashSet();
        if (existingBills.Count > 0)
        {
            var selectedIndexes = selectedBills
                .Select(b => ViewModel.Bills.IndexOf(b))
                .Where(index => index >= 0)
                .OrderBy(index => index)
                .ToList();
            var nextSelectionIndex = selectedIndexes.Count == 0 ? -1 : selectedIndexes[0];

            foreach (var selectedBill in selectedBills.Where(b => existingBills.Contains(b.Id)))
            {
                var dueDate = selectedBill.DueDate.Date;
                var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == selectedBill.Id && s.DueDate == dueDate);
                if (status is null)
                {
                    status = new Finora.Models.BillOccurrenceStatus
                    {
                        BillId = selectedBill.Id,
                        DueDate = dueDate
                    };
                    db.BillOccurrenceStatuses.Add(status);
                }

                status.IsSkipped = true;
                status.MatchNote = "Skipped manually";
            }

            db.SaveChanges();
            ViewModel.LoadDashboard();
            SelectBillAtOrBefore(nextSelectionIndex);
        }
    }

    private void DeleteSelectedBillSeries()
    {
        var selectedBills = BillsGrid.SelectedItems.OfType<BillRow>().ToList();
        if (selectedBills.Count == 0 && ViewModel.SelectedBill is not null)
        {
            selectedBills.Add(ViewModel.SelectedBill);
        }

        if (selectedBills.Count == 0)
        {
            MessageBox.Show("Choose a bill series to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        var ids = selectedBills.Select(b => b.Id).Distinct().ToList();
        var bills = db.Bills
            .Where(b => ids.Contains(b.Id))
            .OrderBy(b => b.Name)
            .ToList();

        if (bills.Count == 0)
        {
            MessageBox.Show("Bill series not found.");
            return;
        }

        var billNames = string.Join(Environment.NewLine, bills.Select(b => $"- {b.Name}"));
        var result = MessageBox.Show(
            $"Delete the entire bill series and all of its weekly/monthly occurrences?{Environment.NewLine}{Environment.NewLine}{billNames}",
            "Delete bill series",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var selectedIndexes = selectedBills
            .Select(b => ViewModel.Bills.IndexOf(b))
            .Where(index => index >= 0)
            .OrderBy(index => index)
            .ToList();
        var nextSelectionIndex = selectedIndexes.Count == 0 ? -1 : selectedIndexes[0];

        var statuses = db.BillOccurrenceStatuses
            .Where(s => ids.Contains(s.BillId))
            .ToList();

        RestoreMatchedBillAdjustments(db, statuses);
        db.BillOccurrenceStatuses.RemoveRange(statuses);
        db.Bills.RemoveRange(bills);
        db.SaveChanges();
        ViewModel.SyncBillsBudget();
        ViewModel.LoadDashboard();
        SelectBillAtOrBefore(nextSelectionIndex);
    }

    private void SelectBillAtOrBefore(int index)
    {
        if (index < 0 || ViewModel.Bills.Count == 0)
        {
            return;
        }

        var nextIndex = Math.Min(index, ViewModel.Bills.Count - 1);
        ViewModel.SelectedBill = ViewModel.Bills[nextIndex];
        BillsGrid.SelectedItem = ViewModel.SelectedBill;
        BillsGrid.ScrollIntoView(ViewModel.SelectedBill);
    }

    private static void RestoreMatchedBillAdjustments(FinoraDbContext db, IEnumerable<Finora.Models.BillOccurrenceStatus> statuses)
    {
        foreach (var status in statuses)
        {
            if (status.MatchedTransactionId is not { } transactionId ||
                string.IsNullOrWhiteSpace(status.OriginalTransactionDescription))
            {
                continue;
            }

            var transaction = db.Transactions.FirstOrDefault(t => t.Id == transactionId);
            if (transaction is null)
            {
                continue;
            }

            transaction.Description = status.OriginalTransactionDescription;
            if (status.OriginalTransactionCategoryId is not null)
            {
                transaction.CategoryId = status.OriginalTransactionCategoryId.Value;
            }

            transaction.TransferId = Guid.TryParse(status.OriginalTransactionTransferId, out var transferId)
                ? transferId
                : null;
        }
    }

    private void All_Click(object sender, RoutedEventArgs e) => ViewModel.AllTransactions();

    private void Weekly_Click(object sender, RoutedEventArgs e) => ViewModel.SummaryPeriod = "Weekly";

    private void Monthly_Click(object sender, RoutedEventArgs e) => ViewModel.SummaryPeriod = "Monthly";
    private void CalendarWeekly_Click(object sender, RoutedEventArgs e) => ViewModel.SummaryPeriod = "Weekly";
    private void CalendarMonthly_Click(object sender, RoutedEventArgs e) => ViewModel.SummaryPeriod = "Monthly";

    private void PreviousWeek_Click(object sender, RoutedEventArgs e) => ViewModel.PreviousSummaryPage();

    private void CurrentWeek_Click(object sender, RoutedEventArgs e) => ViewModel.CurrentSummaryPage();

    private void NextWeek_Click(object sender, RoutedEventArgs e) => ViewModel.NextSummaryPage();

    private void EditTransaction_Click(object sender, RoutedEventArgs e) => EditSelectedTransaction();

    private void MakeBillFromTransaction_Click(object sender, RoutedEventArgs e) => MakeBillOrRecurringPaymentFromSelectedTransaction();

    private void AddRecurringPaymentAsBill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not RecurringPaymentRow recurringPayment)
        {
            return;
        }

        if (recurringPayment.IsAlreadyBill)
        {
            MessageBox.Show("This recurring payment is already in bills.");
            return;
        }

        if (!ViewModel.CreateBillFromRecurringPayment(recurringPayment))
        {
            MessageBox.Show("Could not add this recurring payment as a bill.");
            return;
        }

        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
    }

    private void TransactionsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedTransaction();

    private void EditSelectedTransaction()
    {
        if (ViewModel.SelectedTransaction is null)
        {
            MessageBox.Show("Choose a transaction to edit.");
            return;
        }

        ShowDialogAndRefresh(new AddTransactionWindow(ViewModel.SelectedTransaction.Id));
    }

    private void MakeBillOrRecurringPaymentFromSelectedTransaction()
    {
        if (ViewModel.SelectedTransaction is null)
        {
            MessageBox.Show("Choose a transaction to make into a bill or recurring payment.");
            return;
        }

        if (ViewModel.SelectedTransaction.Amount >= 0)
        {
            MessageBox.Show("Choose a spending transaction to make into a bill or recurring payment.");
            return;
        }

        ShowDialogAndRefresh(new BillWindow(ViewModel.NextPayDate, ViewModel.SelectedTransaction));
    }

    private void TransactionsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nestedMatch = FindVisualChild<T>(child);
            if (nestedMatch is not null)
            {
                return nestedMatch;
            }
        }

        return null;
    }

    private void AccountsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(source);
        if (scrollViewer is null)
        {
            return;
        }

        if (e.Delta < 0)
        {
            scrollViewer.LineDown();
            scrollViewer.LineDown();
        }
        else
        {
            scrollViewer.LineUp();
            scrollViewer.LineUp();
        }

        e.Handled = true;
    }

    private void DeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedTransactions();
    }

    private void DeleteSelectedTransactions()
    {
        var selectedTransactions = TransactionsGrid.SelectedItems.OfType<TransactionRow>().ToList();
        if (selectedTransactions.Count == 0 && ViewModel.SelectedTransaction is not null)
        {
            selectedTransactions.Add(ViewModel.SelectedTransaction);
        }

        if (selectedTransactions.Count == 0)
        {
            MessageBox.Show("Choose a transaction to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        var ids = selectedTransactions.Select(t => t.Id).ToList();
        var transferIds = selectedTransactions
            .Where(t => t.TransferId is { } transferId && transferId != Guid.Empty)
            .Select(t => t.TransferId!.Value)
            .Distinct()
            .ToList();

        var transactions = db.Transactions
            .Where(t => ids.Contains(t.Id) || (t.TransferId != null && transferIds.Contains(t.TransferId.Value)))
            .ToList();

        db.Transactions.RemoveRange(transactions);
        db.SaveChanges();
        ViewModel.LoadDashboard();
    }

    private void Budget_Click(object sender, RoutedEventArgs e) => ShowDialogAndRefresh(new BudgetWindow(ViewModel));


    private void WhatIfBudget_Click(object sender, RoutedEventArgs e)
    {
        var window = new WhatIfBudgetWindow(ViewModel)
        {
            Owner = this
        };
        if (window.ShowDialog() == true)
        {
            ViewModel.LoadDashboard();
        }
    }

    private void MakeBudgetForMe_Click(object sender, RoutedEventArgs e)
    {
        // Open BudgetWindow pre-filled with the suggestion so the user can
        // review, adjust any figure, then Save — nothing is committed until they click Save.
        var win = new BudgetWindow(ViewModel) { Owner = this };
        win.PreloadSuggestion();
        if (win.ShowDialog() == true)
        {
            ViewModel.LoadDashboard();
        }
    }

    private void SyncBillsToBudget_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SyncBillsBudget();
        ViewModel.LoadDashboard();
    }

    private void MatchPaidBills_Click(object sender, RoutedEventArgs e)
    {
        var matched = ViewModel.ApplyBillAutopayMatches();
        ViewModel.LoadDashboard();
        MessageBox.Show(matched == 0
            ? "No unpaid bills matched recent transactions."
            : $"Marked {matched} bill occurrence{(matched == 1 ? "" : "s")} paid from matching transactions.");
    }

    private void ClearBudget_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear saved budget category amounts? Weekly income will be kept.",
            "Clear saved budget",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        ViewModel.ResetSavedBudget();
    }

    private void AddTransactionToBudget_Click(object sender, RoutedEventArgs e)
    {
        var selectedTransactions = TransactionsGrid.SelectedItems.OfType<TransactionRow>().ToList();
        if (selectedTransactions.Count == 0 && ViewModel.SelectedTransaction is not null)
        {
            selectedTransactions.Add(ViewModel.SelectedTransaction);
        }

        if (selectedTransactions.Count == 0)
        {
            MessageBox.Show("Choose a transaction to add to the budget.");
            return;
        }

        var added = 0;
        foreach (var transaction in selectedTransactions)
        {
            if (ViewModel.AddTransactionToBudget(transaction.Id))
            {
                added++;
            }
        }

        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
    }

    private void NeedsCategory_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleUncategorisedTransactions();
    }

    private void AllTransactionTypes_Click(object sender, RoutedEventArgs e) => ViewModel.ShowAllTransactionTypes();

    private void SpendingTransactions_Click(object sender, RoutedEventArgs e) => ViewModel.ShowSpendingTransactions();

    private void IncomeTransactions_Click(object sender, RoutedEventArgs e) => ViewModel.ShowIncomeTransactions();

    private void TransferTransactions_Click(object sender, RoutedEventArgs e) => ViewModel.ShowTransferTransactions();

    private void ToggleCoverTransfers_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCoverTransfers();

    private void AssignCategory_Click(object sender, RoutedEventArgs e)
    {
        var selectedTransactions = TransactionsGrid.SelectedItems.OfType<TransactionRow>().ToList();
        if (selectedTransactions.Count == 0 && ViewModel.SelectedTransaction is not null)
        {
            selectedTransactions.Add(ViewModel.SelectedTransaction);
        }

        if (selectedTransactions.Count == 0)
        {
            MessageBox.Show("Choose one or more transactions to categorise.");
            return;
        }

        var window = new CategoryPickerWindow { Owner = this };
        if (window.ShowDialog() != true)
        {
            return;
        }

        using var db = new FinoraDbContext();
        var ids = selectedTransactions.Select(t => t.Id).Distinct().ToList();
        var transactions = db.Transactions.Where(t => ids.Contains(t.Id)).ToList();
        foreach (var transaction in transactions)
        {
            transaction.CategoryId = window.SelectedCategoryId;
        }

        db.SaveChanges();
        ViewModel.LoadDashboard();
    }

    private void CleanupBillAdjustments_Click(object sender, RoutedEventArgs e)
    {
        var cleaned = ViewModel.CleanupBillAdjustments();
        ViewModel.LoadDashboard();
        MessageBox.Show(cleaned == 0
            ? "No bill-like balance adjustments needed cleanup."
            : $"Cleaned up {cleaned} bill-like balance adjustment{(cleaned == 1 ? "" : "s")}.");
    }

    private void ApplyReviewedBillMatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BillMatchReviewRow review)
        {
            return;
        }

        if (!ViewModel.ApplyReviewedBillMatch(review))
        {
            MessageBox.Show("Could not apply this bill match.");
        }
    }

    private void UndoLastBillCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.UndoLastBillCleanup())
        {
            MessageBox.Show("No cleaned bill adjustment could be undone.");
        }
    }

    private void UnmarkBillPayment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BillPaymentHistoryRow history)
        {
            return;
        }

        if (MessageBox.Show($"Mark '{history.BillName}' due {history.DueDate:dd/MM/yyyy} as unpaid again?",
                "Mark unpaid", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!ViewModel.MarkBillOccurrenceUnpaid(history.BillId, history.DueDate))
        {
            MessageBox.Show("Could not find this bill payment to undo.");
        }
    }

    private void ExportBudget_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export budget to Excel",
            FileName = $"Evergrove Budget {DateTime.Today:yyyy-MM-dd}.csv",
            Filter = "Excel CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var lines = new List<string>
        {
            CsvRow("Evergrove weekly budget"),
            CsvRow("Exported", DateTime.Now.ToString("g", CultureInfo.CurrentCulture)),
            string.Empty,
            CsvRow("Summary", "Amount"),
            CsvRow("Budget total", ViewModel.BudgetTotal),
            CsvRow("Income", ViewModel.WeeklyIncome),
            CsvRow("Bills", ViewModel.BudgetBills),
            CsvRow("Essentials", ViewModel.BudgetEssentials),
            CsvRow("Savings", ViewModel.BudgetSavings),
            CsvRow("Unplanned", ViewModel.BudgetUnplanned),
            CsvRow("Leftover", ViewModel.BudgetLeftover),
            string.Empty,
            CsvRow("Bucket", "Goes toward", "Transfer to", "Per week", "Based on")
        };

        foreach (var row in ViewModel.BudgetBreakdownRows)
        {
            lines.Add(CsvRow(row.Bucket, row.Name, row.TransferTo, row.Amount, row.Detail));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show($"Exported budget to:{Environment.NewLine}{dialog.FileName}");
    }

    private void ExportCashForecast_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export cash forecast",
            FileName = $"Evergrove Cash Forecast {DateTime.Today:yyyy-MM-dd}.csv",
            Filter = "Excel CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var lines = new List<string>
        {
            CsvRow("Evergrove cash forecast"),
            CsvRow("Exported", DateTime.Now.ToString("g", CultureInfo.CurrentCulture)),
            CsvRow("Range", ViewModel.ForecastRange),
            CsvRow("Low point", ViewModel.ForecastLowPointSummary),
            CsvRow("End balance", ViewModel.ForecastEndBalanceSummary),
            string.Empty,
            CsvRow("Date", "Event", "Change", "Projected")
        };

        foreach (var row in ViewModel.CashForecastRows)
        {
            lines.Add(CsvRow(row.Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture), row.Description, row.Change, row.ProjectedBalance));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show($"Exported cash forecast to:{Environment.NewLine}{dialog.FileName}");
    }

    private void ExportTransactionsCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export transactions",
            FileName = $"Evergrove Transactions {DateTime.Today:yyyy-MM-dd}.csv",
            Filter = "Excel CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var lines = new List<string>
        {
            CsvRow("Date", "Description", "Amount", "Account", "Category")
        };

        foreach (var row in ViewModel.Transactions)
        {
            lines.Add(CsvRow(
                row.Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
                row.Description,
                row.Amount,
                row.AccountName,
                row.CategoryName));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show($"Exported {ViewModel.Transactions.Count} transactions to:{Environment.NewLine}{dialog.FileName}");
    }

    private void ExportStatement_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export statement",
            FileName = $"Evergrove Statement {DateTime.Today:yyyy-MM-dd}.csv",
            Filter = "Excel CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        using var db = new FinoraDbContext();
        var allTransactions = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .OrderByDescending(t => t.Date)
            .ToList();

        // Exclude internal movements (transfers, balance adjustments, Up sync artifacts)
        // to match what the app uses for spending/income calculations.
        var realTransactions = allTransactions
            .Where(t => !TransactionClassification.IsInternalMovement(t))
            .ToList();

        var lines = new List<string>
        {
            CsvRow("Date", "Description", "Amount", "Account", "Category", "Type")
        };

        foreach (var t in realTransactions)
        {
            var type = t.AmountCents >= 0 ? "Income" : "Spending";
            lines.Add(CsvRow(
                t.Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
                t.Description,
                t.AmountDollars,
                t.Account?.Name ?? "",
                t.Category?.Name ?? "",
                type));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show($"Exported {realTransactions.Count} transactions (transfers and balance adjustments excluded) to:{Environment.NewLine}{dialog.FileName}");
    }

    private void ApplyNormalBudgetTemplate_Click(object sender, RoutedEventArgs e) => ApplyBudgetTemplate("Normal");

    private void ApplyLeanBudgetTemplate_Click(object sender, RoutedEventArgs e) => ApplyBudgetTemplate("Lean");

    private void ApplyDebtPayoffBudgetTemplate_Click(object sender, RoutedEventArgs e) => ApplyBudgetTemplate("Debt payoff");

    private void ApplyBudgetTemplate(string template)
    {
        if (ViewModel.WeeklyIncome <= 0)
        {
            MessageBox.Show("Set weekly income before previewing a budget template.");
            return;
        }

        ViewModel.PreviewBudgetTemplate(template);
    }

    private void ApplyBudgetTemplateSuggestion_Click(object sender, RoutedEventArgs e) => ViewModel.ApplyBudgetTemplateSuggestion();

    private void ClearBudgetTemplateSuggestion_Click(object sender, RoutedEventArgs e) => ViewModel.ClearBudgetTemplateSuggestion();

    private void LoadAllTransactions_Click(object sender, RoutedEventArgs e) => ViewModel.LoadAllTransactions();

    private void ToggleUnnecessary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DailyTrackerTransactionRow row)
        {
            return;
        }

        ViewModel.ToggleTransactionUnnecessary(row.Id, !row.IsUnnecessary);
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Dashboard";
    private void NavTransactions_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Transactions";
    private void NavCategories_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Categories";
    private void NavPlanning_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Planning";
    private void NavBills_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Bills";
    private void NavSubscriptions_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Subscriptions";
    private void NavCalendar_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Calendar";
    private void NavReports_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Reports";
    private void NavBudget_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Budget";
    private void NavDebts_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Debts";
    private void NavTools_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Tools";
    private void NavGoals_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Goals";
    private void NavDaily_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedNavSection = "Daily";

    private void GoToNavTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string target } && !string.IsNullOrWhiteSpace(target))
        {
            ViewModel.SelectedNavSection = target;
        }
    }

    private void IgnoreRecurringPayment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not RecurringPaymentRow row)
        {
            return;
        }

        ViewModel.IgnoreSubscription(row);
    }

    private void DeleteRecurringPayment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not RecurringPaymentRow row)
        {
            return;
        }

        ViewModel.DeleteSubscriptionTransactions(row);
        ViewModel.LoadDashboard();
    }

    private void RecurringPaymentsGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        var rows = RecurringPaymentsGrid.SelectedItems.OfType<RecurringPaymentRow>().ToList();
        if (rows.Count == 0 && ViewModel.SelectedRecurringPayment is not null)
        {
            rows.Add(ViewModel.SelectedRecurringPayment);
        }

        foreach (var row in rows)
        {
            ViewModel.DeleteSubscriptionTransactions(row);
        }

        ViewModel.LoadDashboard();
        e.Handled = true;
    }

    private void MoreActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ResetSavedBudget_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear saved budget category amounts? Weekly income will be kept.", "Clear budget", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        ViewModel.ResetSavedBudget();
    }

    private void ExportMonthlyReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export monthly report",
            FileName = $"Evergrove Monthly Report {DateTime.Today:yyyy-MM}.csv",
            Filter = "Excel CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var lines = new List<string>
        {
            CsvRow("Evergrove monthly report"),
            CsvRow("Exported", DateTime.Now.ToString("g", CultureInfo.CurrentCulture)),
            CsvRow("Period", ViewModel.SummaryDateRange),
            string.Empty,
            CsvRow("Summary", "Amount"),
            CsvRow("Income", ViewModel.PeriodIncome),
            CsvRow("Spending", ViewModel.PeriodSpending),
            CsvRow("Bills owed", ViewModel.BillsOwedTotal),
            CsvRow("Debt remaining", ViewModel.DebtTotal),
            CsvRow("Savings", ViewModel.SavingsTotal),
            string.Empty,
            CsvRow("Budget variance", "Budgeted", "Actual", "Difference", "Status")
        };

        foreach (var row in ViewModel.BudgetVarianceRows)
        {
            lines.Add(CsvRow(row.Category, row.Budgeted, row.Actual, row.Difference, row.Status));
        }

        lines.Add(string.Empty);
        lines.Add(CsvRow("Cash forecast", "Event", "Change", "Projected"));
        foreach (var row in ViewModel.CashForecastRows)
        {
            lines.Add(CsvRow(row.Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture), row.Description, row.Change, row.ProjectedBalance));
        }

        lines.Add(string.Empty);
        lines.Add(CsvRow("Debt payoff", "Balance", "Payment", "Months", "Estimated paid off"));
        foreach (var row in ViewModel.DebtPayoffPlanRows)
        {
            lines.Add(CsvRow(row.Name, row.Balance, row.MinimumPayment, row.MonthsRemaining, row.EstimatedPaidOffDisplay));
        }

        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show($"Exported monthly report to:{Environment.NewLine}{dialog.FileName}");
    }

    private void EditBudgetRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BudgetBreakdownRow row)
        {
            return;
        }

        EditBudgetRow(row);
    }

    private void RemoveBudgetRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BudgetBreakdownRow row)
        {
            return;
        }

        if (row.AccountId.HasValue)
        {
            var confirm = MessageBox.Show($"Remove target from \"{row.Bucket}\"?", "Remove Target", MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes) return;
            ViewModel.RemoveAccountTarget(row.AccountId.Value);
            ViewModel.LoadDashboard();
            return;
        }

        ViewModel.SetBudgetBreakdownIncluded(row.ExclusionKey, false);
        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
    }

    private void BudgetBreakdownRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: BudgetBreakdownRow row })
        {
            e.Handled = true;
            EditBudgetRow(row);
        }
    }

    private void EditBudgetRow(BudgetBreakdownRow row)
    {
        // Use the most meaningful name as the starting value for the rename dialog
        string currentName;
        if (row.AccountId.HasValue)
            currentName = row.Bucket;
        else if (row.SavingsGoalId.HasValue)
            currentName = row.TransferTo;
        else
            currentName = row.Name;

        var dialog = new RenameBudgetRowWindow(currentName) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.NewName))
        {
            return;
        }

        ViewModel.RenameBudgetRow(row, dialog.NewName);
        ViewModel.LoadDashboard();
    }

    private void AddCustomBudgetItem_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.AddCustomBudgetItem())
        {
            MessageBox.Show("Enter a name and a weekly amount greater than $0.");
            return;
        }

        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
    }

    private void AddSavingsRecommendationToBudget_Click(object sender, RoutedEventArgs e)
    {
        var amount = ViewModel.SavingsRecommendationAmount;
        if (amount <= 0)
        {
            MessageBox.Show("Enter a weekly savings amount greater than $0.");
            return;
        }
        ViewModel.SaveBudget(ViewModel.WeeklyIncome, ViewModel.BudgetBills, ViewModel.BudgetEssentials, amount, ViewModel.BudgetUnplanned);
        ViewModel.IgnoreSavingsRecommendation();
        ViewModel.LoadDashboard();
    }

    private void IgnoreSavingsRecommendation_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IgnoreSavingsRecommendation();
    }

    private void DeclineSavingsRecommendation_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DeclineSavingsRecommendation();
    }

    private void RestoreBudgetSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (BudgetSnapshotsGrid.SelectedItem is not BudgetSnapshotRow snapshot)
        {
            MessageBox.Show("Choose a budget snapshot to restore.");
            return;
        }

        ViewModel.RestoreBudgetSnapshot(snapshot);
        ViewModel.LoadDashboard();
    }

    private void AddTransactionRule_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.AddTransactionRule())
        {
            MessageBox.Show("Enter matching text and choose a category.");
        }
    }

    private void ApplyTransactionRules_Click(object sender, RoutedEventArgs e)
    {
        var updated = ViewModel.ApplyTransactionRules();
        MessageBox.Show(updated == 0
            ? "No transactions needed updating."
            : $"Updated {updated} transaction{(updated == 1 ? "" : "s")}.");
    }

    private void AddCategoryLimit_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.AddCategoryLimit())
            MessageBox.Show("Choose a category and enter a valid dollar amount.");
    }

    private void DeleteCategoryLimit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string category)
            ViewModel.DeleteCategoryLimit(category);
    }

    private void SearchTransactions_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchTransactions();
    }

    private void BrowseCsvImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select bank CSV export",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            CsvFilePathBox.Text = dialog.FileName;
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var path = CsvFilePathBox.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            CsvImportStatusLabel.Text = "Select a CSV file first.";
            return;
        }
        var account = CsvImportAccountPicker.SelectedItem as AccountRow;
        if (account is null)
        {
            CsvImportStatusLabel.Text = "Select an account first.";
            return;
        }
        var (imported, skipped, error) = ImportCsvTransactions(path, account.Id);
        if (error is not null)
        {
            CsvImportStatusLabel.Text = $"Error: {error}";
            return;
        }
        CsvImportStatusLabel.Text = $"Imported {imported} transaction{(imported == 1 ? "" : "s")}{(skipped > 0 ? $", {skipped} skipped (already exist)" : "")}.";
        if (imported > 0)
            ViewModel.LoadDashboard();
    }

    private (int imported, int skipped, string? error) ImportCsvTransactions(string filePath, int accountId)
    {
        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length < 2) return (0, 0, "File has no data rows.");

            var headers = ParseCsvRow(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
            var dateCol = FindCsvColumn(headers, "date", "transaction date", "settled date", "value date", "posted date");
            var descCol = FindCsvColumn(headers, "description", "narrative", "details", "payee", "merchant", "memo", "reference");
            var amtCol  = FindCsvColumn(headers, "amount", "transaction amount", "net amount", "debit/credit");
            var debitCol  = FindCsvColumn(headers, "debit", "withdrawals", "debit amount");
            var creditCol = FindCsvColumn(headers, "credit", "deposits", "credit amount");

            if (dateCol < 0) return (0, 0, "Could not find a Date column (tried: date, transaction date, settled date).");
            if (descCol < 0) return (0, 0, "Could not find a Description column (tried: description, narrative, payee, memo).");
            if (amtCol < 0 && (debitCol < 0 || creditCol < 0))
                return (0, 0, "Could not find an Amount column (tried: amount, debit, credit).");

            using var db = new FinoraDbContext();
            var existingKeys = db.Transactions
                .Where(t => t.AccountId == accountId)
                .Select(t => new { t.Date, t.AmountCents, t.Description })
                .ToList()
                .Select(t => $"{t.Date:yyyyMMdd}|{t.AmountCents}|{t.Description}")
                .ToHashSet();

            var imported = 0;
            var skipped = 0;
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvRow(line);
                if (cols.Count <= Math.Max(dateCol, descCol)) continue;
                if (!DateTime.TryParse(cols[dateCol].Trim(), out var date)) continue;
                var desc = cols[descCol].Trim();

                decimal amount;
                if (amtCol >= 0 && amtCol < cols.Count)
                {
                    var raw = cols[amtCol].Trim().Replace("$", "").Replace(",", "");
                    if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out amount)) continue;
                }
                else
                {
                    decimal debit = 0, credit = 0;
                    if (debitCol >= 0 && debitCol < cols.Count)
                        decimal.TryParse(cols[debitCol].Trim().Replace("$", "").Replace(",", ""),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out debit);
                    if (creditCol >= 0 && creditCol < cols.Count)
                        decimal.TryParse(cols[creditCol].Trim().Replace("$", "").Replace(",", ""),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out credit);
                    amount = credit - debit;
                }

                var amountCents = (int)Math.Round(amount * 100);
                var key = $"{date:yyyyMMdd}|{amountCents}|{desc}";
                if (!existingKeys.Add(key)) { skipped++; continue; }

                db.Transactions.Add(new Finora.Models.Transaction
                {
                    Date = date,
                    Description = desc,
                    AmountCents = amountCents,
                    AccountId = accountId
                });
                imported++;
            }
            if (imported > 0) db.SaveChanges();
            return (imported, skipped, null);
        }
        catch (Exception ex)
        {
            return (0, 0, ex.Message);
        }
    }

    private static int FindCsvColumn(List<string> headers, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            var idx = headers.IndexOf(c);
            if (idx >= 0) return idx;
        }
        return -1;
    }

    private static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        var inQuote = false;
        var sb = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuote && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuote = !inQuote;
            }
            else if (ch == ',' && !inQuote) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    private void BackupDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Back up Evergrove database",
            FileName = $"evergrove-backup-{DateTime.Today:yyyy-MM-dd}.db",
            Filter = "Evergrove database (*.db)|*.db",
            DefaultExt = ".db",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.Copy(FinoraDbContext.DatabasePath, dialog.FileName, overwrite: true);
        MessageBox.Show($"Backed up database to:{Environment.NewLine}{dialog.FileName}");
    }

    private void RestoreDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore Evergrove database",
            Filter = "Evergrove database (*.db)|*.db|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = MessageBox.Show(
            "Restore this database backup? Current local data will be replaced.",
            "Restore database",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var backupPath = $"{FinoraDbContext.DatabasePath}.before-restore-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (File.Exists(FinoraDbContext.DatabasePath))
        {
            File.Copy(FinoraDbContext.DatabasePath, backupPath, overwrite: true);
        }

        File.Copy(dialog.FileName, FinoraDbContext.DatabasePath, overwrite: true);
        ViewModel.LoadDashboard();
        MessageBox.Show($"Database restored. Previous database backup:{Environment.NewLine}{backupPath}");
    }

    private static string CsvRow(params object?[] values)
    {
        return string.Join(",", values.Select(value => CsvEscape(value switch
        {
            decimal amount => amount.ToString("0.00", CultureInfo.CurrentCulture),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        })));
    }

    private static string CsvEscape(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e) => ShowCategoryWindow(null);

    private void CategoriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        EditCategory_Click(sender, e);
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory is null)
        {
            MessageBox.Show("Choose a category to edit.");
            return;
        }

        ShowCategoryWindow(ViewModel.SelectedCategory.Id);
    }

    private void ShowCategoryWindow(int? categoryId) => ShowDialogAndRefresh(new CategoryWindow(categoryId));

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory is null)
        {
            MessageBox.Show("Choose a category to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        if (db.Transactions.Any(t => t.CategoryId == ViewModel.SelectedCategory.Id))
        {
            MessageBox.Show("This category is used by transactions. Move or delete those transactions first.");
            return;
        }

        var category = db.Categories.FirstOrDefault(c => c.Id == ViewModel.SelectedCategory.Id);
        if (category is not null)
        {
            db.Categories.Remove(category);
            db.SaveChanges();
            ViewModel.LoadDashboard();
        }
    }

    private void AddDebt_Click(object sender, RoutedEventArgs e) => ShowDebtWindow(null);

    private void DebtsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        EditDebt_Click(sender, e);
    }

    private void EditDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDebt is null)
        {
            MessageBox.Show("Choose a debt to edit.");
            return;
        }

        ShowDebtWindow(ViewModel.SelectedDebt.Id);
    }

    private void ShowDebtWindow(int? debtId)
    {
        var window = new DebtWindow(debtId) { Owner = this };
        if (window.ShowDialog() == true)
        {
            ViewModel.RefreshAfterDebtChange();
        }
    }

    private void DeleteDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDebt is null)
        {
            MessageBox.Show("Choose a debt to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        var debt = db.Debts.FirstOrDefault(d => d.Id == ViewModel.SelectedDebt.Id);
        if (debt is not null)
        {
            db.Debts.Remove(debt);
            db.SaveChanges();
            ViewModel.RefreshAfterDebtChange();
        }
    }

    private void NestedDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var scrollViewer = FindVisualParent<ScrollViewer>(sender as DependencyObject);
        if (scrollViewer is null)
        {
            return;
        }

        e.Handled = true;
        scrollViewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        });
    }

    private void DebtStrategyIncludeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadDebtStrategiesForCurrentSelection();
    }

    private void SelectAllDebtStrategies_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllDebtStrategySelections(true);

    private void ClearDebtStrategies_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllDebtStrategySelections(false);

    private void AddSavingsGoal_Click(object sender, RoutedEventArgs e) => ShowSavingsGoalWindow(null);

    private void SavingsGoalsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        EditSavingsGoal_Click(sender, e);
    }

    private void BudgetAddGoal_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedNavSection = "Goals";
    }

    private void AddSavingsGoalToBudget_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSavingsGoal is null)
        {
            MessageBox.Show("Choose a goal to add to the budget.");
            return;
        }

        if (!ViewModel.AddSavingsGoalToBudget(ViewModel.SelectedSavingsGoal.Id))
        {
            MessageBox.Show("Goal not found.");
            return;
        }

        ViewModel.SaveSuggestedBudget();
        ViewModel.LoadDashboard();
    }

    private void EditSavingsGoal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSavingsGoal is null)
        {
            MessageBox.Show("Choose a savings goal to edit.");
            return;
        }

        ShowSavingsGoalWindow(ViewModel.SelectedSavingsGoal.Id);
    }

    private void ShowSavingsGoalWindow(int? goalId) => ShowDialogAndRefresh(new SavingsGoalWindow(goalId));

    private void DeleteSavingsGoal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSavingsGoal is null)
        {
            MessageBox.Show("Choose a savings goal to delete.");
            return;
        }

        using var db = new FinoraDbContext();
        var goal = db.SavingsGoals.FirstOrDefault(g => g.Id == ViewModel.SelectedSavingsGoal.Id);
        if (goal is not null)
        {
            db.SavingsGoals.Remove(goal);
            db.SaveChanges();
            var remainingCents = db.SavingsGoals.AsNoTracking()
                .Sum(g => (int?)g.WeeklyContributionCents) ?? 0;
            ViewModel.BudgetSavings = Math.Round(remainingCents / 100m, 2);
            ViewModel.SaveSuggestedBudget();
            ViewModel.LoadDashboard();
        }
    }

    private void NextPaydayPicker_SelectedDateChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoadingPayday || NextPaydayPicker.SelectedDate is not DateTime nextPayDate)
        {
            return;
        }

        ViewModel.SetNextPayDate(nextPayDate);
    }

    private void TransactionsGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelectedTransactions();
            e.Handled = true;
        }
    }

    private void AccountsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelectedAccounts();
            e.Handled = true;
        }
    }

    private void BillsGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelectedBills();
            e.Handled = true;
        }
    }

    private void ShowDialogAndRefresh(Window window)
    {
        window.Owner = this;
        if (window.ShowDialog() == true)
        {
            if (window is BillWindow)
            {
                ViewModel.SyncBillsBudget();
            }
            ViewModel.LoadDashboard();
        }
    }
}
