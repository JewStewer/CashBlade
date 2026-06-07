using System.Windows;
using System.Windows.Controls;
using Finora.Data;
using Finora.Models;
using Microsoft.EntityFrameworkCore;

namespace Finora.Views;

public class CategoryWindow : Window
{
    private readonly int? _categoryId;
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _typeBox = new();

    public CategoryWindow(int? categoryId = null)
    {
        _categoryId = categoryId;
        Title = categoryId is null ? "Add Category" : "Edit Category";
        Width = 420;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinHeight = 330;
        MinWidth = 420;
        Background = System.Windows.Media.Brushes.White;

        _typeBox.ItemsSource = Enum.GetValues(typeof(CategoryType));
        _typeBox.SelectedItem = CategoryType.Expense;

        Content = BuildContent();

        if (categoryId is not null)
        {
            LoadCategory(categoryId.Value);
        }
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "Category name" });
        panel.Children.Add(_nameBox);
        panel.Children.Add(new TextBlock { Text = "Type", Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(_typeBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = new Button { Content = "Save", Width = 90, Height = 34 };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        return panel;
    }

    private void LoadCategory(int id)
    {
        using var db = new FinoraDbContext();
        var category = db.Categories.FirstOrDefault(c => c.Id == id);
        if (category is null)
        {
            MessageBox.Show("Category not found.");
            Close();
            return;
        }

        _nameBox.Text = category.Name;
        _typeBox.SelectedItem = category.Type;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a category name.");
            return;
        }

        if (_typeBox.SelectedItem is not CategoryType type)
        {
            MessageBox.Show("Choose a category type.");
            return;
        }

        using var db = new FinoraDbContext();
        if (db.Categories.Any(c => EF.Functions.Like(c.Name, name) && c.Id != _categoryId))
        {
            MessageBox.Show("A category with this name already exists.");
            return;
        }

        var category = _categoryId is null
            ? new Category()
            : db.Categories.FirstOrDefault(c => c.Id == _categoryId.Value);

        if (category is null)
        {
            MessageBox.Show("Category not found.");
            return;
        }

        category.Name = name;
        category.Type = type;

        if (_categoryId is null)
        {
            db.Categories.Add(category);
        }

        db.SaveChanges();
        DialogResult = true;
        Close();
    }
}
