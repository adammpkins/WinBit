using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Settings;
using WinBit.Core.Sharing;
using WinBit.Core.Tags;

namespace WinBit.Views.Dialogs;

public sealed partial class AddMagnetDialog : ContentDialog
{
    private readonly ITorrentSessionService _session;
    private readonly ICategoryService _categories;
    private readonly ITagService _tags;
    private readonly IShareLimitOverrideService _shareLimitOverrides;
    private readonly ISettingsService _settings;
    private readonly Dictionary<string, Category> _categoryMap = new(StringComparer.OrdinalIgnoreCase);

    public AddMagnetDialog(
        ITorrentSessionService session,
        ICategoryService categories,
        ITagService tags,
        IShareLimitOverrideService shareLimitOverrides,
        ISettingsService settings,
        nint ownerHwnd)
    {
        InitializeComponent();
        _session = session;
        _categories = categories;
        _tags = tags;
        _shareLimitOverrides = shareLimitOverrides;
        _settings = settings;
        OwnerHwnd = ownerHwnd;

        PrimaryButtonClick += OnAddClicked;
        _ = LoadCategoriesAsync();
        _ = LoadTagsAsync();
    }

    public nint OwnerHwnd { get; }

    /// <summary>Preloads the magnet URI box — used when the app is activated with a magnet argument.</summary>
    public void SetMagnet(string magnetUri) => MagnetBox.Text = magnetUri;

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
        try
        {
            var magnet = MagnetBox.Text.Trim();
            var savePath = SavePathBox.Text.Trim();

            if (!magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("Enter a valid magnet URI starting with 'magnet:'.");
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                savePath = _settings.Current.Downloads.DefaultSavePath
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var tags = TagsList.SelectedItems.OfType<string>().ToArray();

            var result = await _session.AddAsync(new AddTorrentParams
            {
                Source = magnet,
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
}
