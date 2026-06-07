using System.Windows;
using Finora.Data;
using Finora.Models;
using Finora.Services;
using Finora.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Finora.Views;

public partial class BillWindow : Window
{
    private readonly int? _billId;
    private readonly DateTime _nextPayDate;

    public BillWindow(DateTime nextPayDate)
    {
        _nextPayDate = nextPayDate.Date;

        InitializeComponent();
        LoadOptions();
        DueDatePicker.SelectedDate = DateTime.Today;
        NameBox.Focus();
    }

    public BillWindow(int billId, DateTime nextPayDate) : this(nextPayDate)
    {
        _billId = billId;
        Title = "Edit Bill";
        HeaderText.Text = "Edit Bill";
        LoadBill(billId);
    }

    public BillWindow(DateTime nextPayDate, RecurringPaymentRow recurringPayment) : this(nextPayDate)
    {
        Title = "Create Bill";
        HeaderText.Text = "Create Bill";
        NameBox.Text = recurringPayment.Name;
        AmountBox.Text = recurringPayment.Amount.ToString("0.00");
        DueDatePicker.SelectedDate = recurringPayment.NextExpected;
        FrequencyBox.SelectedItem = ParseFrequency(recurringPayment.Frequency);
        SelectAccount(recurringPayment.AccountName);
        NameBox.SelectAll();
    }

    public BillWindow(DateTime nextPayDate, TransactionRow transaction) : this(nextPayDate)
    {
        Title = "Create Bill / Recurring Payment";
        HeaderText.Text = "Create Bill / Recurring Payment";
        NameBox.Text = GetBillName(transaction.Description);
        AmountBox.Text = Math.Abs(transaction.Amount).ToString("0.00");
        DueDatePicker.SelectedDate = transaction.Date;
        PaidBox.IsChecked = true;
        SelectAccount(transaction.AccountName);
        NameBox.SelectAll();
    }

    private void LoadOptions()
    {
        using var db = new FinoraDbContext();

        AccountBox.ItemsSource = db.Accounts.OrderBy(a => a.Name).ToList();
        AccountBox.SelectedIndex = 0;
        var debtOptions = new List<DebtOption> { new(0, "None") };
        debtOptions.AddRange(db.Debts.OrderBy(d => d.Name).Select(d => new DebtOption(d.Id, d.Name)));
        DebtBox.ItemsSource = debtOptions;
        DebtBox.SelectedValue = 0;

        FrequencyBox.ItemsSource = Enum.GetValues(typeof(BillFrequency));
        FrequencyBox.SelectedItem = BillFrequency.Monthly;
    }

    private void LoadBill(int billId)
    {
        using var db = new FinoraDbContext();

        var bill = db.Bills.FirstOrDefault(b => b.Id == billId);

        if (bill is null)
        {
            MessageBox.Show("Bill not found.");
            Close();
            return;
        }

        NameBox.Text = bill.Name;
        AccountBox.SelectedValue = bill.AccountId;
        DebtBox.SelectedValue = bill.DebtId ?? 0;
        AmountBox.Text = bill.AmountDollars.ToString("0.00");
        PaymentMatchTextBox.Text = bill.PaymentMatchText;
        FrequencyBox.SelectedItem = bill.Frequency;
        AutoPayBox.IsChecked = bill.IsAutoPay;

        // Show the next upcoming unpaid occurrence, not the raw base DueDate.
        // This means opening Insurance after paying May 24 shows June 24 automatically.
        var displayDate = FindNextUnpaidOccurrence(db, bill);
        DueDatePicker.SelectedDate = displayDate;
        PaidBox.IsChecked = db.BillOccurrenceStatuses
            .Where(s => s.BillId == bill.Id && s.DueDate == displayDate.Date)
            .Select(s => s.IsPaid)
            .FirstOrDefault();
    }

    private void SelectAccount(string accountName)
    {
        if (!string.IsNullOrWhiteSpace(accountName))
        {
            foreach (var item in AccountBox.Items)
            {
                if (item is Account account && string.Equals(account.Name, accountName, StringComparison.OrdinalIgnoreCase))
                {
                    AccountBox.SelectedItem = account;
                    return;
                }
            }
        }

        foreach (var item in AccountBox.Items)
        {
            if (item is Account account && string.Equals(account.Name, "Bills", StringComparison.OrdinalIgnoreCase))
            {
                AccountBox.SelectedItem = account;
                return;
            }
        }
    }

    private static BillFrequency ParseFrequency(string frequency)
    {
        return Enum.TryParse<BillFrequency>(frequency, out var parsed)
            ? parsed
            : BillFrequency.Monthly;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a bill name.");
            return;
        }

        if (AccountBox.SelectedValue is not int accountId)
        {
            MessageBox.Show("Choose an account.");
            return;
        }

        if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("Enter a positive bill amount.");
            return;
        }

        if (FrequencyBox.SelectedItem is not BillFrequency frequency)
        {
            MessageBox.Show("Choose a frequency.");
            return;
        }

        using var db = new FinoraDbContext();

        var bill = _billId is null
            ? new Bill()
            : db.Bills.FirstOrDefault(b => b.Id == _billId.Value);

        if (bill is null)
        {
            MessageBox.Show("Bill not found.");
            return;
        }

        bill.Name = name;
        bill.AccountId = accountId;
        bill.DebtId = DebtBox.SelectedValue is int debtId && debtId > 0 ? debtId : null;
        bill.AmountDollars = amount;
        bill.PaymentMatchText = PaymentMatchTextBox.Text.Trim();
        bill.IsAutoPay = AutoPayBox.IsChecked == true;
        bill.NextPayDate = _nextPayDate;
        bill.Frequency = frequency;
        bill.IsPaid = false;

        // The occurrence date the user has selected (could differ from the base DueDate).
        var selectedOccurrenceDate = (DueDatePicker.SelectedDate ?? DateTime.Today).Date;

        // If marking this occurrence as paid, advance the base DueDate to the next cycle
        // so the editor and bills list always show the upcoming date on the next open.
        if (PaidBox.IsChecked == true)
        {
            bill.DueDate = Finora.ViewModels.MainViewModel.GetNextBillDueDate(selectedOccurrenceDate, frequency);
        }
        else
        {
            bill.DueDate = selectedOccurrenceDate;
        }

        if (_billId is null)
        {
            db.Bills.Add(bill);
        }

        db.SaveChanges();
        // Record the paid/unpaid status against the occurrence the user selected, not the advanced base date.
        SaveOccurrencePaidStatus(db, bill.Id, selectedOccurrenceDate, PaidBox.IsChecked == true);

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Returns the first occurrence of <paramref name="bill"/> from its DueDate forward
    /// that has not been marked paid. Falls back to the base DueDate if nothing found.
    /// </summary>
    private static DateTime FindNextUnpaidOccurrence(FinoraDbContext db, Bill bill)
    {
        var date = bill.DueDate.Date;
        var today = DateTime.Today;

        // Advance past dates that are more than one cycle in the past, so we don't
        // get stuck on a very old anchor (e.g. a bill created years ago).
        while (date < today.AddDays(-35))
        {
            date = Finora.ViewModels.MainViewModel.GetNextBillDueDate(date, bill.Frequency);
        }

        // If this occurrence is already paid, keep advancing until we find the next unpaid one.
        for (var guard = 0; guard < 24; guard++)
        {
            var isPaid = db.BillOccurrenceStatuses
                .Where(s => s.BillId == bill.Id && s.DueDate == date)
                .Select(s => s.IsPaid)
                .FirstOrDefault();
            if (!isPaid) break;
            date = Finora.ViewModels.MainViewModel.GetNextBillDueDate(date, bill.Frequency);
        }

        return date;
    }

    private static void SaveOccurrencePaidStatus(FinoraDbContext db, int billId, DateTime dueDate, bool isPaid)
    {
        var status = db.BillOccurrenceStatuses.FirstOrDefault(s => s.BillId == billId && s.DueDate == dueDate);
        if (status is null)
        {
            status = new BillOccurrenceStatus
            {
                BillId = billId,
                DueDate = dueDate
            };
            db.BillOccurrenceStatuses.Add(status);
        }

        status.IsPaid = isPaid;
        status.PaidOn = isPaid ? DateTime.Today : null;
        status.MatchNote = isPaid ? "Marked paid manually" : string.Empty;
        if (db.Bills.FirstOrDefault(b => b.Id == billId) is { } bill)
        {
            DebtPaymentMatcher.ApplyBillDebtPaymentStatus(db, bill, dueDate, isPaid);
        }

        db.SaveChanges();
    }

    private static string GetBillName(string description)
    {
        var cleaned = description.Trim();
        var separatorIndex = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex > 0 ? cleaned[..separatorIndex] : cleaned;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed record DebtOption(int Id, string Name);
}
