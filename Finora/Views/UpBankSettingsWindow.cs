using System.Windows;
using System.Windows.Controls;
using Finora.Services;

namespace Finora.Views;

public class UpBankSettingsWindow : Window
{
    private readonly UpBankSyncService _syncService = new();
    private readonly PasswordBox _tokenBox = new();
    private TextBlock _repairStatus = new();
    private Button _repairButton = new();
    private System.Threading.CancellationTokenSource? _repairCts;

    public UpBankSettingsWindow()
    {
        Title = "Up Bank API";
        Width = 520;
        Height = 380;
        MinWidth = 520;
        MinHeight = 380;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildContent()
        };
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "Up Bank API",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Paste a personal access token from Up. Evergrove stores it locally and uses it to import settled transactions.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock { Text = "Access token", Margin = new Thickness(0, 8, 0, 3) });
        _tokenBox.Height = 32;
        _tokenBox.Password = _syncService.GetAccessToken();
        panel.Children.Add(_tokenBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Debt payments match against each debt's Up payment match text, or the debt name when the match text is blank.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 14, 0, 0)
        });

        panel.Children.Add(new Separator { Margin = new Thickness(0, 18, 0, 14) });

        panel.Children.Add(new TextBlock
        {
            Text = "Fix transaction order",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "One-time repair for transactions imported before this fix existed — re-fetches each Up transaction's exact time so same-day imports display in the right order. New imports are already correct; this only affects past ones. Safe to run more than once.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _repairButton = new Button { Content = "Repair transaction order", Width = 200, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        _repairButton.Click += RepairButton_Click;
        panel.Children.Add(_repairButton);

        _repairStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(_repairStatus);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var save = new Button { Content = "Save", Width = 90, Height = 34 };
        save.Click += (_, _) =>
        {
            _syncService.SaveAccessToken(_tokenBox.Password);
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        return panel;
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repairCts is not null)
        {
            _repairCts.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(_tokenBox.Password))
        {
            _repairStatus.Text = "Add and save your Up Bank token first.";
            return;
        }

        _repairCts = new System.Threading.CancellationTokenSource();
        _repairButton.Content = "Cancel repair";
        _repairStatus.Text = "Starting…";

        var progress = new Progress<(int Done, int Total)>(p =>
            _repairStatus.Text = $"Repairing… {p.Done} / {p.Total}");

        try
        {
            var result = await _syncService.BackfillSettledTimestampsAsync(progress, _repairCts.Token);
            _repairStatus.Text = result.Updated == 0 && result.Skipped == 0 && result.Failed == 0
                ? "Nothing to repair — all imported transactions already have a precise timestamp."
                : $"Done. Fixed {result.Updated}, skipped {result.Skipped} (no longer on Up), {result.Failed} failed (will retry next run).";
        }
        catch (OperationCanceledException)
        {
            _repairStatus.Text = "Repair cancelled. Already-fixed transactions were kept — run again to continue.";
        }
        catch (Exception ex)
        {
            _repairStatus.Text = $"Repair failed: {ex.Message}";
        }
        finally
        {
            _repairCts = null;
            _repairButton.Content = "Repair transaction order";
        }
    }
}
