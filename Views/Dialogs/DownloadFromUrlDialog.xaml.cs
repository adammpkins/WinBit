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
/// Fetches a <c>.torrent</c> file from an HTTP(S) URL via <see cref="UrlDownloader"/>, writes
/// the bytes to a temp file so <c>ITorrentSessionService.AddAsync</c> can load it, then
/// deletes the temp file. All failure paths (bad scheme, 404, size-capped, parse error)
/// surface inline in an <c>InfoBar</c>.
/// </summary>
public sealed partial class DownloadFromUrlDialog : ContentDialog
{
    private readonly ITorrentSessionService _session;
    private readonly UrlDownloader _downloader;
    private readonly ICategoryService _categories;
    private readonly ITagService _tags;
    private readonly IShareLimitOverrideService _shareLimitOverrides;
    private readonly ISettingsService _settings;
    private readonly Dictionary<string, Category> _categoryMap = new(StringComparer.OrdinalIgnoreCase);

    public DownloadFromUrlDialog(
        ITorrentSessionService session,
        UrlDownloader downloader,
        ICategoryService categories,
        ITagService tags,
        IShareLimitOverrideService shareLimitOverrides,
        ISettingsService settings,
        nint ownerHwnd)
    {
        InitializeComponent();
        _session = session;
        _downloader = downloader;
        _categories = categories;
        _tags = tags;
        _shareLimitOverrides = shareLimitOverrides;
        _settings = settings;
        OwnerHwnd = ownerHwnd;

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

    public nint OwnerHwnd { get; }

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
        SavePathBox.Text = TmmPathResolver.Resolve(global, categoryName, name => _categoryMap.TryGetValue(name, out var c) ? c : null);
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SavePathBox.Text = folder.Path;
        }
    }

    private async void OnAddClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        string? tempPath = null;
        try
        {
            var urlText = UrlBox.Text.Trim();
            var savePath = SavePathBox.Text.Trim();

            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
            {
                ShowError("Enter a valid absolute URL.");
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                ShowError("Pick a save path.");
                args.Cancel = true;
                return;
            }

            ErrorBar.IsOpen = false;
            BusyPanel.Visibility = Visibility.Visible;

            var download = await _downloader.DownloadAsync(url);
            if (!download.IsSuccess)
            {
                ShowError(download.Error ?? "Download failed.");
                args.Cancel = true;
                return;
            }

            tempPath = Path.Combine(Path.GetTempPath(), $"winbit-{Guid.NewGuid():N}.torrent");
            await File.WriteAllBytesAsync(tempPath, download.Value!);

            var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var tags = TagsList.SelectedItems.OfType<string>().ToArray();

            var add = await _session.AddAsync(new AddTorrentParams
            {
                Source = tempPath,
                SavePath = savePath,
                Category = category,
                Tags = tags,
                StartImmediately = StartImmediatelyCheck.IsChecked ?? true,
            });

            if (!add.IsSuccess)
            {
                ShowError(add.Error ?? "Add failed.");
                args.Cancel = true;
                return;
            }

            await PersistShareLimitOverrideAsync(add.Value!);
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            BusyPanel.Visibility = Visibility.Collapsed;
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch { /* best-effort temp cleanup */ }
            }
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
}
