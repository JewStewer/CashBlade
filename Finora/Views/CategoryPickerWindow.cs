using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;

namespace Finora.Views;

public class CategoryPickerWindow : Window
{
    private readonly ComboBox _categoryBox = new();

    public int SelectedCategoryId { get; private set; }

    public CategoryPickerWindow()
    {
        Title = "Assign Category";
        Width = 360;
        Height = 180;
        MinWidth = 320;
        MinHeight = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        using var db = new FinoraDbContext();
        _categoryBox.ItemsSource = db.Categories
            .Where(c => c.Type == CategoryType.Expense)
            .OrderBy(c => c.Name)
            .ToList();
        _categoryBox.DisplayMemberPath = "Name";
        _categoryBox.SelectedValuePath = "Id";
        _categoryBox.SelectedIndex = 0;
        _categoryBox.Height = 34;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Assign selected transactions to",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(_categoryBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = new Button { Content = "Assign", Width = 90, Height = 34 };
        save.Click += (_, _) =>
        {
            SelectedCategoryId = _categoryBox.SelectedValue is int id ? id : 0;
            DialogResult = SelectedCategoryId > 0;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
