using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.Tags;

namespace WinBit.Views.Dialogs;

/// <summary>
/// Adds a torrent from a local <c>.torrent</c> file. Shows a nested file tree of the
/// torrent's contents, a save-path combobox backed by an MRU list of recent roots, and
/// a Start-immediately checkbox.
/// </summary>
public sealed partial class AddTorrentDialog : ContentDialog
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ICategoryService _categories;
    private readonly ITagService _tags;
    private readonly IShareLimitOverrideService _shareLimitOverrides;
    private readonly ObservableCollection<PreviewNode> _previewRoots = new();
    private readonly Dictionary<string, Category> _categoryMap = new(StringComparer.OrdinalIgnoreCase);
    private string? _pickedPath;

    public AddTorrentDialog(
        ITorrentSessionService session,
        ISettingsService settings,
        ICategoryService categories,
        ITagService tags,
        IShareLimitOverrideService shareLimitOverrides,
        nint ownerHwnd)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _categories = categories;
        _tags = tags;
        _shareLimitOverrides = shareLimitOverrides;
        OwnerHwnd = ownerHwnd;

        FilesTree.ItemsSource = _previewRoots;
        SavePathCombo.ItemsSource = _settings.Current.UiState.RecentSavePaths;
        if (_settings.Current.UiState.RecentSavePaths.Count > 0)
        {
            SavePathCombo.SelectedIndex = 0;
        }

        PrimaryButtonClick += OnAddClicked;
        _ = LoadCategoriesAsync();
        _ = LoadTagsAsync();
    }

    private async Task LoadTagsAsync()
    {
        var all = await _tags.GetAllAsync();
        TagsList.ItemsSource = all;
        TagsEmptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTagsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = TagsList.SelectedItems.OfType<string>().ToArray();
        TagsButton.Content = selected.Length switch
        {
            0 => "(no tags)",
            1 => selected[0],
            _ => $"{selected.Length} tags selected",
        };
    }

    private async Task LoadCategoriesAsync()
    {
        var all = await _categories.GetAllAsync();
        _categoryMap.Clear();

        CategoryCombo.Items.Clear();
        CategoryCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
        foreach (var c in all)
        {
            _categoryMap[c.Name] = c;
            CategoryCombo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Name });
        }

        CategoryCombo.SelectedIndex = 0;
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var categoryName = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(categoryName))
        {
            return;
        }

        var global = _settings.Current.Downloads.DefaultSavePath
                     ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        SavePathCombo.Text = TmmPathResolver.Resolve(global, categoryName, name => _categoryMap.TryGetValue(name, out var c) ? c : null);
    }

    public nint OwnerHwnd { get; }

    private async void OnPickTorrentClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        picker.FileTypeFilter.Add(".torrent");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await PreloadTorrentAsync(file.Path);
    }

    /// <summary>
    /// Pre-populates the dialog with a torrent from <paramref name="path"/> — same work as a
    /// successful file-picker result. Used by the window-level drag-drop handler.
    /// </summary>
    public async Task PreloadTorrentAsync(string path)
    {
        _pickedPath = path;
        TorrentPathBox.Text = path;
        ErrorBar.IsOpen = false;

        try
        {
            var torrent = await MonoTorrent.Torrent.LoadAsync(path);
            TorrentNameText.Text = torrent.Name;
            PopulatePreview(torrent);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't read .torrent: {ex.Message}");
            TorrentNameText.Text = string.Empty;
            _previewRoots.Clear();
        }
    }

    private void PopulatePreview(MonoTorrent.Torrent torrent)
    {
        _previewRoots.Clear();
        var root = new PreviewNode { Name = torrent.Name, IconGlyph = "\uE8B7" };

        foreach (var file in torrent.Files)
        {
            var parts = file.Path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var name = parts[i];
                var isFile = i == parts.Length - 1;
                var existing = current.Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
                if (existing is null)
                {
                    existing = new PreviewNode
                    {
                        Name = name,
                        IconGlyph = isFile ? "\uE7C3" : "\uE8B7",
                        SizeText = isFile ? FormatBytes(file.Length) : string.Empty,
                    };
                    current.Children.Add(existing);
                }
                current = existing;
            }
        }

        _previewRoots.Add(root);
    }

    private async void OnBrowseSaveClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SavePathCombo.Text = folder.Path;
        }
    }

    private async void OnAddClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (string.IsNullOrWhiteSpace(_pickedPath))
            {
                ShowError("Pick a .torrent file first.");
                args.Cancel = true;
                return;
            }

            var savePath = (SavePathCombo.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(savePath))
            {
                savePath = _settings.Current.Downloads.DefaultSavePath
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var tags = TagsList.SelectedItems.OfType<string>().ToArray();

            var result = await _session.AddAsync(new AddTorrentParams
            {
                Source = _pickedPath,
                SavePath = savePath,
                Category = category,
                Tags = tags,
                StartImmediately = StartImmediatelyCheck.IsChecked ?? true,
            });

            if (!result.IsSuccess)
            {
                ShowError(result.Error ?? "Add failed.");
                args.Cancel = true;
                return;
            }

            await PersistShareLimitOverrideAsync(result.Value!);
            await _settings.UpdateAsync(s => RecentPathsHelper.PushMru(s.UiState.RecentSavePaths, savePath));
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task PersistShareLimitOverrideAsync(WinBit.Core.Common.TorrentId id)
    {
        var action = ShareLimitDialogHelpers.ReadAction(ActionCombo);
        var ratio = RatioLimitBox.Value > 0 ? (double?)RatioLimitBox.Value : null;
        var seeding = SeedingMinutesBox.Value > 0 ? (TimeSpan?)TimeSpan.FromMinutes(SeedingMinutesBox.Value) : null;
        var inactive = InactiveMinutesBox.Value > 0 ? (TimeSpan?)TimeSpan.FromMinutes(InactiveMinutesBox.Value) : null;

        if (ratio is null && seeding is null && inactive is null && action == ShareLimitAction.Default)
        {
            return;
        }

        await _shareLimitOverrides.UpsertAsync(new PerTorrentShareLimitOverride
        {
            Id = id,
            RatioLimit = ratio,
            SeedingTimeLimit = seeding,
            InactiveSeedingTimeLimit = inactive,
            Action = action,
        });
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = -1;
        do { v /= 1024; u++; } while (v >= 1024 && u < units.Length - 1);
        return $"{v:0.##} {units[u]}";
    }
}

public sealed class PreviewNode
{
    public string Name { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE8B7";
    public ObservableCollection<PreviewNode> Children { get; } = new();
}
