using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Finora.Data;

namespace Finora.Views;

public class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        Title = "Diagnostics";
        Width = 760;
        Height = 560;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBox
        {
            Text = BuildDiagnosticsText(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(16)
        };

        var close = new Button
        {
            Content = "Close",
            Width = 90,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 16, 16)
        };
        close.Click += (_, _) => Close();

        var panel = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        panel.Children.Add(text);
        Content = panel;
    }

    private static string BuildDiagnosticsText()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(localAppData, "Cashglade", "cashglade.db");
        var logPath = Path.Combine(localAppData, "Cashglade", "startup.log");
        var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Cashglade.exe - Shortcut.lnk");
        var startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Cashglade", "Cashglade.lnk");

        using var db = new FinoraDbContext();
        var debtIdColumn = SafeColumnCheck(db, "Bills", "DebtId");
        var upTransactionColumn = SafeColumnCheck(db, "Transactions", "UpTransactionId");

        return string.Join(Environment.NewLine, new[]
        {
            "Evergrove diagnostics",
            $"Version: {version}",
            $"Executable: {exePath}",
            $"Database: {dbPath}",
            $"Database exists: {File.Exists(dbPath)}",
            $"Startup log: {logPath}",
            $"Desktop shortcut: {desktopShortcut}",
            $"Start Menu shortcut: {startMenuShortcut}",
            "",
            "Schema",
            $"Bills.DebtId: {(debtIdColumn ? "present" : "missing")}",
            $"Transactions.UpTransactionId: {(upTransactionColumn ? "present" : "missing")}",
            "",
            "Latest startup log",
            ReadLogTail(logPath)
        });
    }

    private static bool SafeColumnCheck(FinoraDbContext db, string table, string column)
    {
        try
        {
            return SchemaRepair.ColumnExists(db, table, column);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadLogTail(string path)
    {
        if (!File.Exists(path))
        {
            return "No startup log found.";
        }

        var lines = File.ReadLines(path).TakeLast(60);
        return string.Join(Environment.NewLine, lines);
    }
}
