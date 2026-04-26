using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Common;
using WinBit.Core.Filters;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.Tags;
using WinBit.Services;
using WinBit.ViewModels.Transfers;
using WinBit.Views.Dialogs;
using WinUI.TableView;

namespace WinBit.Views.Transfers;

public sealed partial class TransfersPage : Page
{
    private readonly ISettingsService _settings;
    // Cached delegate so AddHandler/RemoveHandler use the same instance.
    private readonly TappedEventHandler _gridTappedHandler;

    public TransfersViewModel ViewModel { get; }

    public TransfersPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TransfersViewModel>();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _gridTappedHandler = new TappedEventHandler(OnTransfersGridTapped);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static Visibility VisibleIfTrue(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(_settings.Current.UiState.TransfersGrid);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        PropertiesPivot.SelectionChanged += OnPropertiesPivotSelectionChanged;
        // handledEventsToo: true — TableView's internal cell selection marks Tapped as
        // Handled before it bubbles; without this flag our handler would never fire.
        TransfersGrid.AddHandler(UIElement.TappedEvent, _gridTappedHandler, handledEventsToo: true);
        await ViewModel.RefreshFilterOptionsAsync();
        PopulateFilterTree();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransfersViewModel.TrackerHostOptions))
        {
            PopulateFilterTree();
        }
    }

    private void PopulateFilterTree()
    {
        var priorFilter = (FilterTree.SelectedNode?.Content as FilterNode)?.Filter;

        FilterTree.RootNodes.Clear();

        var all = new TreeViewNode { Content = new FilterNode("All torrents", TransferFilter.All), IsExpanded = true };
        FilterTree.RootNodes.Add(all);

        var statusRoot = new TreeViewNode { Content = new FilterNode("Status", null), IsExpanded = true };
        foreach (var (label, status) in StatusFilters)
        {
            statusRoot.Children.Add(new TreeViewNode { Content = new FilterNode(label, TransferFilter.ForStatus(status)) });
        }
        FilterTree.RootNodes.Add(statusRoot);

        var categoriesRoot = new TreeViewNode { Content = new FilterNode("Categories", null), IsExpanded = true };
        categoriesRoot.Children.Add(new TreeViewNode { Content = new FilterNode("(Uncategorized)", TransferFilter.Uncategorized) });
        foreach (var category in ViewModel.CategoryOptions)
        {
            categoriesRoot.Children.Add(new TreeViewNode { Content = new FilterNode(category.Name, TransferFilter.ForCategory(category.Name)) });
        }
        FilterTree.RootNodes.Add(categoriesRoot);

        var tagsRoot = new TreeViewNode { Content = new FilterNode("Tags", null), IsExpanded = true };
        foreach (var tag in ViewModel.TagOptions)
        {
            tagsRoot.Children.Add(new TreeViewNode { Content = new FilterNode(tag, TransferFilter.ForTag(tag)) });
        }
        FilterTree.RootNodes.Add(tagsRoot);

        var trackersRoot = new TreeViewNode { Content = new FilterNode("Trackers", null), IsExpanded = true };
        foreach (var host in ViewModel.TrackerHostOptions)
        {
            trackersRoot.Children.Add(new TreeViewNode { Content = new FilterNode(host, TransferFilter.ForTrackerHost(host)) });
        }
        FilterTree.RootNodes.Add(trackersRoot);

        FilterTree.SelectedNode = FindNode(priorFilter) ?? all;
    }

    private TreeViewNode? FindNode(TransferFilter? target)
    {
        if (target is null)
        {
            return null;
        }
        foreach (var root in FilterTree.RootNodes)
        {
            if ((root.Content as FilterNode)?.Filter == target)
            {
                return root;
            }
            foreach (var child in root.Children)
            {
                if ((child.Content as FilterNode)?.Filter == target)
                {
                    return child;
                }
            }
        }
        return null;
    }

    private static readonly (string Label, TransferStatus Status)[] StatusFilters =
    {
        ("Downloading", TransferStatus.Downloading),
        ("Seeding", TransferStatus.Seeding),
        ("Completed", TransferStatus.Completed),
        ("Paused", TransferStatus.Paused),
        ("Active", TransferStatus.Active),
        ("Inactive", TransferStatus.Inactive),
        ("Errored", TransferStatus.Errored),
    };

    private void OnFilterInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && node.Content is FilterNode filter && filter.Filter is not null)
        {
            ViewModel.ApplyFilter(filter.Filter);
        }
    }

    private async void OnManageCategoriesClicked(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(CategoriesAndTagsDialog.Tab.Categories);

    private async void OnManageTagsClicked(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(CategoriesAndTagsDialog.Tab.Tags);

    private async Task ShowEditorAsync(CategoriesAndTagsDialog.Tab tab)
    {
        var dialog = new CategoriesAndTagsDialog(
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            tab)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();

        await ViewModel.RefreshFilterOptionsAsync();
        PopulateFilterTree();
    }

    private sealed record FilterNode(string Label, TransferFilter? Filter)
    {
        public override string ToString() => Label;
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        PropertiesPivot.SelectionChanged -= OnPropertiesPivotSelectionChanged;
        TransfersGrid.RemoveHandler(UIElement.TappedEvent, _gridTappedHandler);
        var layout = CaptureLayout();
        await _settings.UpdateAsync(s => s.UiState.TransfersGrid = layout);
    }

    private void OnPropertiesPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Peers is index 2 in the Pivot (General=0, Trackers=1, Peers=2, Content=3, Speed=4)
        var isPeersActive = PropertiesPivot.SelectedIndex == 2;
        var isTrackersActive = PropertiesPivot.SelectedIndex == 1;
        ViewModel.Properties.SetPeersTabActive(isPeersActive);
        ViewModel.Properties.SetTrackersTabActive(isTrackersActive);
        if (isPeersActive || isTrackersActive)
        {
            // SelectedTorrentRow is kept in sync by the TwoWay SelectedItem binding.
            ViewModel.Properties.SetSelectedTorrent(
                (ViewModel.SelectedTorrentRow as TransferRowViewModel)?.Id);
        }
    }

    private void ApplyLayout(TransfersGridLayout layout)
    {
        if (layout.Columns.Count == 0)
        {
            return;
        }

        foreach (var column in TransfersGrid.Columns)
        {
            if (column.Tag is not string tag || !layout.Columns.TryGetValue(tag, out var state))
            {
                continue;
            }

            if (state.Width > 0)
            {
                column.Width = new Microsoft.UI.Xaml.GridLength(state.Width);
            }

            column.Order = state.Order;

            column.SortDirection = state.SortDirection switch
            {
                "Ascending" => SortDirection.Ascending,
                "Descending" => SortDirection.Descending,
                _ => null,
            };
        }
    }

    private TransfersGridLayout CaptureLayout()
    {
        var layout = new TransfersGridLayout();

        foreach (var column in TransfersGrid.Columns)
        {
            if (column.Tag is not string tag)
            {
                continue;
            }

            layout.Columns[tag] = new TransferColumnState
            {
                Width = column.ActualWidth > 0 ? column.ActualWidth : 0,
                Order = column.Order ?? 0,
                SortDirection = column.SortDirection switch
                {
                    SortDirection.Ascending => "Ascending",
                    SortDirection.Descending => "Descending",
                    _ => null,
                },
            };
        }

        return layout;
    }

    private async void OnAddTorrentClicked(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".torrent");
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var dialog = new AddTorrentDialog(
            App.Services.GetRequiredService<ITorrentSessionService>(),
            _settings,
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            hwnd)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.PreloadTorrentAsync(file.Path);
        await dialog.ShowAsync();
    }

    private async void OnAddMagnetClicked(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var dialog = new AddMagnetDialog(
            App.Services.GetRequiredService<ITorrentSessionService>(),
            App.Services.GetRequiredService<ICategoryService>(),
            App.Services.GetRequiredService<ITagService>(),
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            _settings,
            hwnd)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void OnContextMenuOpening(object sender, object e)
    {
        RenameMenuItem.IsEnabled = TransfersGrid.SelectedItems.Count == 1;
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length != 1)
        {
            return;
        }

        var id = ids[0];
        var currentName = Session.GetName(id) ?? string.Empty;
        var dialog = new RenameTorrentDialog(Session, id, currentName)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void OnPauseClicked(object sender, RoutedEventArgs e) =>
        await ForEachSelectedAsync(id => Session.PauseAsync(id));

    private async void OnResumeClicked(object sender, RoutedEventArgs e) =>
        await ForEachSelectedAsync(id => Session.ResumeAsync(id));

    private async void OnForceRecheckClicked(object sender, RoutedEventArgs e) =>
        await ForEachSelectedAsync(id => Session.ForceRecheckAsync(id));

    private async void OnForceReannounceClicked(object sender, RoutedEventArgs e) =>
        await ForEachSelectedAsync(id => Session.ForceReannounceAsync(id));

    private async void OnRemoveClicked(object sender, RoutedEventArgs e) =>
        await ForEachSelectedAsync(id => Session.RemoveAsync(id));

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        foreach (var id in SelectedIds())
        {
            var path = Session.GetSavePath(id);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                await Launcher.LaunchFolderAsync(folder);
            }
            catch
            {
                // best-effort launch; unknown paths or permission errors are silent for now.
            }
        }
    }

    private async void OnSpeedLimitsClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var dialog = new PerTorrentSpeedLimitDialog(Session, ids)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void OnShareLimitsClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var dialog = new PerTorrentShareLimitDialog(
            App.Services.GetRequiredService<IShareLimitOverrideService>(),
            ids)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void OnCopyMagnetClicked(object sender, RoutedEventArgs e)
    {
        var uris = SelectedIds()
            .Select(Session.GetMagnetUri)
            .Where(u => !string.IsNullOrEmpty(u))
            .ToArray();

        if (uris.Length == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(string.Join('\n', uris!));
        Clipboard.SetContent(package);
    }

    private ITorrentSessionService Session =>
        App.Services.GetRequiredService<ITorrentSessionService>();

    private IEnumerable<TorrentId> SelectedIds() =>
        TransfersGrid.SelectedItems.OfType<TransferRowViewModel>().Select(r => r.Id);

    private async Task ForEachSelectedAsync(Func<TorrentId, Task> action)
    {
        foreach (var id in SelectedIds().ToArray())
        {
            await action(id);
        }
    }

    private void OnTransfersGridTapped(object sender, TappedRoutedEventArgs e)
    {
        var row = FindRowViewModel(e.OriginalSource as DependencyObject);
        ViewModel.Properties.SetSelectedTorrent(row?.Id);
    }

    // WinUI 3 doesn't auto-select the row under the cursor when opening a ContextFlyout,
    // unlike classic Windows shell. Without this handler, right-clicking a row leaves
    // SelectedItems empty and every menu action is a silent no-op.
    private void OnTransfersGridRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var row = FindRowViewModel(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        // Always notify the properties panel regardless of prior selection state.
        ViewModel.Properties.SetSelectedTorrent(row.Id);

        if (TransfersGrid.SelectedItems.Contains(row))
        {
            return;
        }

        TransfersGrid.SelectedItems.Clear();
        TransfersGrid.SelectedItems.Add(row);
    }

    private static TransferRowViewModel? FindRowViewModel(DependencyObject? start)
    {
        while (start is not null)
        {
            if (start is FrameworkElement fe && fe.DataContext is TransferRowViewModel vm)
            {
                return vm;
            }
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }
}
