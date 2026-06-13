using System.Windows;
using System.Windows.Controls;
using Finora.Services;

namespace Finora.Views;

public class UpBankSettingsWindow : Window
{
    private readonly UpBankSyncService _syncService = new();
    private readonly PasswordBox _tokenBox = new();

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
}
