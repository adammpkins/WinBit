using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
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
    // Cached delegates so AddHandler/RemoveHandler use the same instances.
    private readonly TappedEventHandler _gridTappedHandler;
    private readonly RightTappedEventHandler _gridRightTappedHandler;
    private TorrentFileEntry? _contextMenuFile;

    public TransfersViewModel ViewModel { get; }

    public TransfersPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TransfersViewModel>();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _gridTappedHandler = new TappedEventHandler(OnTransfersGridTapped);
        _gridRightTappedHandler = new RightTappedEventHandler(OnTransfersGridRightTapped);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static Visibility VisibleIfTrue(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string ContentTabEmptySubtitle(bool hasSelectedTorrent) =>
        hasSelectedTorrent ? "File list will appear shortly." : "Select a torrent to view its contents.";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(_settings.Current.UiState.TransfersGrid);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        PropertiesPivot.SelectionChanged += OnPropertiesPivotSelectionChanged;
        // handledEventsToo: true — TableView's internal cell selection marks Tapped as
        // Handled before it bubbles; without this flag our handler would never fire.
        TransfersGrid.AddHandler(UIElement.TappedEvent, _gridTappedHandler, handledEventsToo: true);
        // TableView also marks RightTapped as Handled, preventing ContextFlyout from
        // opening automatically; we show it explicitly in the handler below.
        TransfersGrid.AddHandler(UIElement.RightTappedEvent, _gridRightTappedHandler, handledEventsToo: true);
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
        TransfersGrid.RemoveHandler(UIElement.RightTappedEvent, _gridRightTappedHandler);
        var layout = CaptureLayout();
        await _settings.UpdateAsync(s => s.UiState.TransfersGrid = layout);
    }

    private void OnPropertiesPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Pivot order: General=0, Trackers=1, Web Seeds=2, Peers=3, Content=4, Pieces=5, Speed=6
        var isTrackersActive = PropertiesPivot.SelectedIndex == 1;
        var isWebSeedsActive = PropertiesPivot.SelectedIndex == 2;
        var isPeersActive = PropertiesPivot.SelectedIndex == 3;
        var isContentActive = PropertiesPivot.SelectedIndex == 4;
        var isPiecesActive = PropertiesPivot.SelectedIndex == 5;
        var isSpeedActive = PropertiesPivot.SelectedIndex == 6;
        ViewModel.Properties.SetTrackersTabActive(isTrackersActive);
        ViewModel.Properties.SetWebSeedsTabActive(isWebSeedsActive);
        ViewModel.Properties.SetPeersTabActive(isPeersActive);
        ViewModel.Properties.SetContentTabActive(isContentActive);
        ViewModel.Properties.SetPiecesTabActive(isPiecesActive);
        ViewModel.Properties.SetSpeedTabActive(isSpeedActive);
        // Read from the control directly — TwoWay SelectedItem binding doesn't fire on first TableView click.
        // Only forward a non-null selection; if SelectedItem is momentarily null (e.g. during rapid
        // tab switches or in FlaUI-simulated clicks), we must not clear _selectedId and cancel a
        // running content poll.
        if ((isTrackersActive || isWebSeedsActive || isPeersActive || isContentActive || isPiecesActive || isSpeedActive)
            && TransfersGrid.SelectedItem is TransferRowViewModel selectedRow)
        {
            ViewModel.Properties.SetSelectedTorrent(selectedRow.Id);
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
        var selectedRow = TransfersGrid.SelectedItems.OfType<TransferRowViewModel>().FirstOrDefault();
        SequentialMenuItem.IsChecked = selectedRow?.IsSequentialDownload ?? false;
        FirstLastPieceMenuItem.IsChecked = selectedRow?.IsFirstLastPiecePriority ?? false;
        ForceStartMenuItem.IsChecked = selectedRow?.IsForceStart ?? false;
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

    private async void OnSequentialDownloadClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length == 0)
            return;

        var desired = SequentialMenuItem.IsChecked;
        var anyFailed = false;
        foreach (var id in ids)
        {
            var result = await Session.SetSequentialDownloadAsync(id, desired);
            if (!result.IsSuccess)
                anyFailed = true;
        }

        if (anyFailed)
            SequentialMenuItem.IsChecked = !desired;
    }

    private async void OnFirstLastPiecePriorityClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length == 0)
            return;

        var desired = FirstLastPieceMenuItem.IsChecked;
        var anyFailed = false;
        foreach (var id in ids)
        {
            var result = await Session.SetFirstLastPiecePriorityAsync(id, desired);
            if (!result.IsSuccess)
                anyFailed = true;
        }

        if (anyFailed)
            FirstLastPieceMenuItem.IsChecked = !desired;
    }

    private async void OnForceStartClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length == 0)
            return;

        var desired = ForceStartMenuItem.IsChecked;
        var anyFailed = false;
        foreach (var id in ids)
        {
            var result = await Session.ForceStartTorrentAsync(id, desired);
            if (!result.IsSuccess)
                anyFailed = true;
        }

        if (anyFailed)
            ForceStartMenuItem.IsChecked = !desired;
    }

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

    private async void OnRelocateClicked(object sender, RoutedEventArgs e)
    {
        if (TransfersGrid.SelectedItem is not TransferRowViewModel row)
            return;

        if (App.MainWindow is null)
            return;

        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        await Session.RelocateTorrentAsync(row.Id, folder.Path);
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

    private async void OnAddPeersClicked(object sender, RoutedEventArgs e)
    {
        var ids = SelectedIds().ToArray();
        if (ids.Length == 0) return;
        var dialog = new AddPeersDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var (ip, port) in dialog.ParsedPeers)
            foreach (var id in ids)
                await Session.AddPeerAsync(id, ip, port);
    }

    private async void OnExportTorrentClicked(object sender, RoutedEventArgs e)
    {
        if (TransfersGrid.SelectedItem is not TransferRowViewModel row) return;
        if (App.MainWindow is null) return;

        var bytes = await Session.ExportTorrentBytesAsync(row.Id);
        if (bytes is null)
        {
            var dialog = new ContentDialog
            {
                Title = "Export failed",
                Content = "Torrent metadata is not yet available. Wait for the download to start.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = row.Name + ".torrent",
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeChoices.Add("Torrent file", new List<string> { ".torrent" });

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await FileIO.WriteBytesAsync(file, bytes);
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

        if (!TransfersGrid.SelectedItems.Contains(row))
        {
            TransfersGrid.SelectedItems.Clear();
            TransfersGrid.SelectedItems.Add(row);
        }

        // TableView marks RightTapped as Handled, so the ContextFlyout cannot open
        // automatically. Show it explicitly at the tap position.
        if (TransfersGrid.ContextFlyout is MenuFlyout flyout)
        {
            flyout.ShowAt(TransfersGrid, e.GetPosition(TransfersGrid));
        }
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

    private void OnContentFileRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Walk up from the tapped element — TableView cells are deeply nested and the
        // OriginalSource may be an inner TextBlock rather than the row container itself.
        var element = e.OriginalSource as DependencyObject;
        TorrentFileEntry? found = null;
        while (element is not null)
        {
            if (element is FrameworkElement fe && fe.DataContext is TorrentFileEntry entry)
            {
                found = entry;
                break;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        _contextMenuFile = found;
    }

    private void OnContentFileContextMenuOpening(object sender, object e)
    {
        ContentRenameMenuItem.IsEnabled = _contextMenuFile is not null;
        ContentRenameFolderMenuItem.IsEnabled = _contextMenuFile?.RelativePath.Contains('/') == true;
        ContentSetPriorityMenuItem.IsEnabled = _contextMenuFile is not null;
    }

    private async void OnSetFilePriorityClicked(object sender, RoutedEventArgs e)
    {
        if (_contextMenuFile is not { } file) return;
        if (sender is not MenuFlyoutItem item) return;
        if (!Enum.TryParse<FileDownloadPriority>(item.Tag?.ToString(), out var priority)) return;
        await ViewModel.Properties.SetFilePriorityAsync(file.Index, priority);
    }

    private async void OnContentFileRenameClicked(object sender, RoutedEventArgs e)
    {
        if (_contextMenuFile is not { } file) return;

        int lastSlash = file.RelativePath.LastIndexOf('/');
        string currentLeaf = lastSlash < 0 ? file.RelativePath : file.RelativePath[(lastSlash + 1)..];

        var dialog = new ContentDialog
        {
            Title = "Rename file",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Resources["ContentDialogMinWidth"] = 400.0;
        dialog.Resources["ContentDialogMaxWidth"] = 600.0;

        var textBox = new TextBox
        {
            Text = currentLeaf,
            PlaceholderText = "New file name",
            SelectionStart = 0,
            SelectionLength = currentLeaf.LastIndexOf('.') is var dot && dot > 0 ? dot : currentLeaf.Length,
        };
        dialog.Content = textBox;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        string newLeaf = textBox.Text.Trim();
        if (string.IsNullOrEmpty(newLeaf) || newLeaf == currentLeaf) return;

        // Preserve the directory prefix and only replace the leaf name.
        string newRelativePath = lastSlash < 0 ? newLeaf : file.RelativePath[..(lastSlash + 1)] + newLeaf;
        await ViewModel.Properties.RenameFileAsync(file.Index, newRelativePath);
    }

    private async void OnContentFolderRenameClicked(object sender, RoutedEventArgs e)
    {
        if (_contextMenuFile is not { } file) return;
        int lastSlash = file.RelativePath.LastIndexOf('/');
        if (lastSlash < 0) return;
        string oldFolderPath = file.RelativePath[..lastSlash];
        int folderNameStart = oldFolderPath.LastIndexOf('/') + 1; // 0 if no parent folder
        string currentFolderName = oldFolderPath[folderNameStart..];

        var dialog = new ContentDialog
        {
            Title = "Rename folder",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        dialog.Resources["ContentDialogMinWidth"] = 400.0;
        dialog.Resources["ContentDialogMaxWidth"] = 600.0;
        var textBox = new TextBox
        {
            Text = currentFolderName,
            PlaceholderText = "New folder name",
            SelectionStart = 0,
            SelectionLength = currentFolderName.Length,
        };
        dialog.Content = textBox;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        string newFolderName = textBox.Text.Trim();
        if (string.IsNullOrEmpty(newFolderName) || newFolderName == currentFolderName) return;

        // Reconstruct full folder path: preserve ancestor prefix, replace only the immediate folder name.
        string newFolderPath = folderNameStart > 0
            ? oldFolderPath[..folderNameStart] + newFolderName
            : newFolderName;
        await ViewModel.Properties.RenameFolderAsync(oldFolderPath, newFolderPath);
    }

    private void OnTrackersGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep SelectedTracker in sync. TableView marks Tapped as Handled internally, so
        // SelectionChanged is the reliable path for tracking which row is active.
        ViewModel.Properties.SelectedTracker = TrackersGrid.SelectedItem as TrackerRowViewModel;
    }

    private async void OnAddTrackerClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AddTrackerDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.Properties.AddTrackerAsync(dialog.TrackerUrl, dialog.Tier);
    }

    private async void OnEditTrackerClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Properties.SelectedTracker is not { } tracker) return;
        var dialog = new EditTrackerDialog(tracker.Url, tracker.Tier) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.Properties.EditTrackerAsync(tracker.Url, dialog.TrackerUrl, dialog.Tier);
    }

    private async void OnRemoveTrackerClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Properties.SelectedTracker is not { } tracker) return;
        await ViewModel.Properties.RemoveTrackerAsync(tracker.Url);
    }

    private void OnWebSeedsGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.Properties.SelectedWebSeed = WebSeedsGrid.SelectedItem as WebSeedRowViewModel;
    }

    private async void OnAddWebSeedClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AddWebSeedDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.Properties.AddWebSeedAsync(dialog.SeedUrl);
    }

    private async void OnRemoveWebSeedClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Properties.SelectedWebSeed is not { } seed) return;
        await ViewModel.Properties.RemoveWebSeedAsync(seed.Url);
    }
}
