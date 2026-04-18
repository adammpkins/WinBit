using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Categories;
using WinBit.Core.Tags;

namespace WinBit.Views.Dialogs;

public sealed partial class CategoriesAndTagsDialog : ContentDialog
{
    public enum Tab { Categories, Tags }

    private readonly ICategoryService _categories;
    private readonly ITagService _tags;
    private readonly ObservableCollection<CategoryRow> _categoryRows = new();
    private readonly ObservableCollection<string> _tagRows = new();

    public CategoriesAndTagsDialog(ICategoryService categories, ITagService tags, Tab initialTab)
    {
        InitializeComponent();
        _categories = categories;
        _tags = tags;

        CategoriesList.ItemsSource = _categoryRows;
        TagsList.ItemsSource = _tagRows;
        EditorPivot.SelectedIndex = initialTab == Tab.Tags ? 1 : 0;

        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _categoryRows.Clear();
        foreach (var c in await _categories.GetAllAsync())
        {
            _categoryRows.Add(new CategoryRow(c.Name, c.SavePath));
        }

        _tagRows.Clear();
        foreach (var t in await _tags.GetAllAsync())
        {
            _tagRows.Add(t);
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoriesList.SelectedItem is CategoryRow row)
        {
            CategoryNameBox.Text = row.Name;
            CategorySavePathBox.Text = row.SavePath ?? string.Empty;
        }
    }

    private async void OnUpsertCategoryClicked(object sender, RoutedEventArgs e)
    {
        var name = CategoryNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowCategoryError("Enter a category name.");
            return;
        }

        var savePath = CategorySavePathBox.Text.Trim();
        try
        {
            await _categories.UpsertAsync(new Category
            {
                Name = name,
                SavePath = string.IsNullOrWhiteSpace(savePath) ? null : savePath,
            });
            CategoryError.IsOpen = false;
            CategoryNameBox.Text = string.Empty;
            CategorySavePathBox.Text = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ShowCategoryError(ex.Message);
        }
    }

    private async void OnRemoveCategoryClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            await _categories.RemoveAsync(name);
            await LoadAsync();
        }
    }

    private async void OnAddTagClicked(object sender, RoutedEventArgs e)
    {
        var name = TagNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowTagError("Enter a tag name.");
            return;
        }

        try
        {
            await _tags.AddAsync(name);
            TagError.IsOpen = false;
            TagNameBox.Text = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ShowTagError(ex.Message);
        }
    }

    private async void OnRemoveTagClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            await _tags.RemoveAsync(name);
            await LoadAsync();
        }
    }

    private void ShowCategoryError(string message)
    {
        CategoryError.Message = message;
        CategoryError.IsOpen = true;
    }

    private void ShowTagError(string message)
    {
        TagError.Message = message;
        TagError.IsOpen = true;
    }

    private sealed record CategoryRow(string Name, string? SavePath)
    {
        public string SavePathText => string.IsNullOrWhiteSpace(SavePath) ? "(default)" : SavePath;
    }
}
