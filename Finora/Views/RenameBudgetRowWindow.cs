using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finora.Views;

public class RenameBudgetRowWindow : Window
{
    private readonly TextBox _nameBox;

    public string NewName { get; private set; } = "";

    public RenameBudgetRowWindow(string currentName)
    {
        Title = "Rename";
        Width = 380;
        Height = 165;
        MinWidth = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x16));

        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(14),
            Padding = new Thickness(16)
        };

        var stack = new StackPanel();

        var label = new TextBlock
        {
            Text = "Name",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };

        _nameBox = new TextBox
        {
            Text = currentName,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x20)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Height = 34,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var saveBtn = new Button
        {
            Content = "Save",
            Width = 80,
            Height = 34,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        saveBtn.Click += (_, _) => { NewName = _nameBox.Text.Trim(); DialogResult = true; Close(); };
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { NewName = _nameBox.Text.Trim(); DialogResult = true; Close(); }
            else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(saveBtn);
        stack.Children.Add(label);
        stack.Children.Add(_nameBox);
        stack.Children.Add(buttons);
        outer.Child = stack;
        Content = outer;

        Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
    }
}
