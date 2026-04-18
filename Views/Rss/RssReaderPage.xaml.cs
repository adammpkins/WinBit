using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.BitTorrent;
using WinBit.Core.Rss;
using WinBit.Core.Settings;

namespace WinBit.Views.Rss;

public sealed partial class RssReaderPage : Page
{
    private readonly IRssService _rss;
    private readonly IRssArticleCache _cache;
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly DispatcherQueue _dispatcher;

    public ObservableCollection<RssTreeItem> TreeItems { get; } = new();
    public ObservableCollection<ArticleItem> Articles { get; } = new();

    private string? _selectedFeedUrl;

    public RssReaderPage()
    {
        InitializeComponent();
        _rss = App.Services.GetRequiredService<IRssService>();
        _cache = App.Services.GetRequiredService<IRssArticleCache>();
        _session = App.Services.GetRequiredService<ITorrentSessionService>();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        FeedTree.ItemsSource = TreeItems;
        ArticleList.ItemsSource = Articles;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshTreeAsync();
        _rss.Changed += OnRssChanged;
        _cache.Updated += OnCacheUpdated;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _rss.Changed -= OnRssChanged;
        _cache.Updated -= OnCacheUpdated;
    }

    private void OnRssChanged(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(async () => await RefreshTreeAsync());
    }

    private void OnCacheUpdated(object? sender, RssArticleCacheUpdatedEventArgs e)
    {
        if (!string.Equals(e.FeedUrl, _selectedFeedUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _dispatcher.TryEnqueue(() => LoadArticles(e.FeedUrl));
    }

    private async Task RefreshTreeAsync()
    {
        var root = await _rss.GetTreeAsync();
        TreeItems.Clear();
        foreach (var child in BuildTree(root).Children)
        {
            TreeItems.Add(child);
        }
    }

    private static RssTreeItem BuildTree(RssFolder folder, string parentPath = "")
    {
        var item = new RssTreeItem
        {
            Label = string.IsNullOrEmpty(folder.Name) ? "Feeds" : folder.Name,
            Glyph = "\uE8B7", // folder
            IsFolder = true,
            FolderPath = parentPath,
        };

        foreach (var sub in folder.Folders)
        {
            var subPath = string.IsNullOrEmpty(parentPath) ? sub.Name : $"{parentPath}/{sub.Name}";
            item.Children.Add(BuildTree(sub, subPath));
        }
        foreach (var feed in folder.Feeds)
        {
            item.Children.Add(new RssTreeItem
            {
                Label = string.IsNullOrWhiteSpace(feed.Title) ? feed.Url : feed.Title!,
                Glyph = "\uE704", // rss
                IsFolder = false,
                FeedUrl = feed.Url,
            });
        }
        return item;
    }

    private void OnFeedInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is RssTreeItem item && !item.IsFolder && item.FeedUrl is not null)
        {
            _selectedFeedUrl = item.FeedUrl;
            ArticleHeader.Text = item.Label;
            LoadArticles(item.FeedUrl);
        }
    }

    private void LoadArticles(string feedUrl)
    {
        Articles.Clear();
        foreach (var a in _cache.Get(feedUrl))
        {
            Articles.Add(new ArticleItem
            {
                Title = a.Title,
                PublishedText = a.PublishedUtc == default ? "" : a.PublishedUtc.ToLocalTime().ToString("g"),
                TorrentUrl = a.TorrentUrl,
                CanDownload = !string.IsNullOrWhiteSpace(a.TorrentUrl),
            });
        }
        EmptyState.Visibility = Articles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAutoDownloaderClicked(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(AutoDownloaderPage));
    }

    private async void OnAddFeedClicked(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            Header = "Feed URL",
            PlaceholderText = "https://example.com/feed.xml",
            Text = "",
        };
        var folderBox = new TextBox
        {
            Header = "Folder (optional, e.g. TV/Shows)",
            Text = "",
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add RSS feed",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel { Spacing = 12, Children = { urlBox, folderBox } },
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var url = urlBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ShowStatus(InfoBarSeverity.Warning, "Invalid URL.", url);
            return;
        }

        await _rss.UpsertFeedAsync(folderBox.Text?.Trim() ?? "", new RssFeedConfig { Url = url });
        ShowStatus(InfoBarSeverity.Success, "Feed added.", url);
    }

    private async void OnDownloadClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ArticleItem item || string.IsNullOrWhiteSpace(item.TorrentUrl))
        {
            return;
        }

        var savePath = _settings.Current.Downloads.DefaultSavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            ShowStatus(InfoBarSeverity.Warning, "Set a default save path in Settings first.", "");
            return;
        }

        var result = await _session.AddAsync(new AddTorrentParams
        {
            Source = item.TorrentUrl!,
            SavePath = savePath,
            StartImmediately = true,
        });

        if (result.IsSuccess)
        {
            ShowStatus(InfoBarSeverity.Success, "Download added.", item.Title);
        }
        else
        {
            ShowStatus(InfoBarSeverity.Error, "Download failed.", result.Error ?? "");
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}

public sealed class RssTreeItem
{
    public string Label { get; set; } = "";
    public string Glyph { get; set; } = "\uE8B7";
    public bool IsFolder { get; set; }
    public string? FeedUrl { get; set; }
    public string FolderPath { get; set; } = "";
    public ObservableCollection<RssTreeItem> Children { get; } = new();
}

public sealed class ArticleItem
{
    public string Title { get; set; } = "";
    public string PublishedText { get; set; } = "";
    public string? TorrentUrl { get; set; }
    public bool CanDownload { get; set; }
}
