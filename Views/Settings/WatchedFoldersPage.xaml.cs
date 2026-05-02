using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinBit.Core.WatchedFolders;

namespace WinBit.Views.Settings;

public sealed partial class WatchedFoldersPage : Page
{
    private readonly IWatchedFolderService _service;

    public WatchedFoldersPage()
    {
        InitializeComponent();
        _service = App.Services.GetRequiredService<IWatchedFolderService>();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var folders = await _service.GetAllAsync();
        var items = folders.Select(f => new WatchedFolderItemViewModel
        {
            Path = f.Path,
            RawPath = f.Path,
            Description = BuildDescription(f),
        }).ToList();

        FolderList.ItemsSource = items;

        var hasItems = items.Count > 0;
        FolderList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string BuildDescription(WatchedFolder f)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.SavePath))
        {
            parts.Add($"Save to: {f.SavePath}");
        }
        if (f.DeleteSourceOnAdd)
        {
            parts.Add("Delete source");
        }
        if (f.StartImmediately)
        {
            parts.Add("Start immediately");
        }
        if (f.Recursive)
        {
            parts.Add("Include subdirectories");
        }
        return parts.Count > 0 ? string.Join(" · ", parts) : "Default options";
    }

    private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var storageFolder = await picker.PickSingleFolderAsync();
        if (storageFolder is null)
        {
            return;
        }

        var folderPath = storageFolder.Path;

        var savePathBox = new TextBox
        {
            PlaceholderText = "Leave blank to use default save location",
            MinWidth = 340,
        };
        var startImmediatelyToggle = new ToggleSwitch
        {
            Header = "Start immediately",
            IsOn = true,
        };
        var recursiveToggle = new ToggleSwitch
        {
            Header = "Include subdirectories",
            IsOn = false,
        };
        var deleteSourceToggle = new ToggleSwitch
        {
            Header = "Delete .torrent file after adding",
            IsOn = true,
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Save path override",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        panel.Children.Add(savePathBox);
        panel.Children.Add(startImmediatelyToggle);
        panel.Children.Add(recursiveToggle);
        panel.Children.Add(deleteSourceToggle);

        var dialog = new ContentDialog
        {
            Title = "Folder options",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Resources["ContentDialogMinWidth"] = 480.0;
        dialog.Resources["ContentDialogMaxWidth"] = 600.0;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var savePath = savePathBox.Text.Trim();
        var folder = new WatchedFolder
        {
            Path = folderPath,
            SavePath = string.IsNullOrWhiteSpace(savePath) ? null : savePath,
            StartImmediately = startImmediatelyToggle.IsOn,
            Recursive = recursiveToggle.IsOn,
            DeleteSourceOnAdd = deleteSourceToggle.IsOn,
        };

        await _service.UpsertAsync(folder);
        await RefreshAsync();
    }

    private async void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            await _service.RemoveAsync(path);
            await RefreshAsync();
        }
    }
}
