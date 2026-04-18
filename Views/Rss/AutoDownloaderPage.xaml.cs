using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinBit.Core.Rss;

namespace WinBit.Views.Rss;

public sealed partial class AutoDownloaderPage : Page, INotifyPropertyChanged
{
    private readonly IAutoDownloaderService _service;
    private readonly IRssService _rss;

    public ObservableCollection<RuleListItem> Rules { get; } = new();
    public ObservableCollection<FeedOption> AllFeeds { get; } = new();

    private RuleListItem? _selected;
    public bool HasSelection => _selected is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AutoDownloaderPage()
    {
        InitializeComponent();
        _service = App.Services.GetRequiredService<IAutoDownloaderService>();
        _rss = App.Services.GetRequiredService<IRssService>();

        RuleList.ItemsSource = Rules;
        FeedsList.ItemsSource = AllFeeds;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshFeedsAsync();
        await RefreshRulesAsync();
        _service.Changed += OnServiceChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _service.Changed -= OnServiceChanged;
    }

    private async void OnServiceChanged(object? sender, EventArgs e)
    {
        await DispatcherQueue.EnqueueAsync(RefreshRulesAsync);
    }

    private async Task RefreshFeedsAsync()
    {
        AllFeeds.Clear();
        var root = await _rss.GetTreeAsync();
        foreach (var url in EnumerateFeedUrls(root))
        {
            AllFeeds.Add(new FeedOption { Url = url });
        }
    }

    private static IEnumerable<string> EnumerateFeedUrls(RssFolder folder)
    {
        foreach (var f in folder.Feeds)
        {
            yield return f.Url;
        }
        foreach (var sub in folder.Folders)
        {
            foreach (var u in EnumerateFeedUrls(sub))
            {
                yield return u;
            }
        }
    }

    private async Task RefreshRulesAsync()
    {
        var selectedName = _selected?.Name;
        Rules.Clear();
        foreach (var rule in await _service.GetAllAsync())
        {
            Rules.Add(new RuleListItem
            {
                Name = rule.Name,
                Enabled = rule.Enabled,
            });
        }
        EmptyList.Visibility = Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectedName is not null)
        {
            var match = Rules.FirstOrDefault(r => string.Equals(r.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                RuleList.SelectedItem = match;
            }
        }
    }

    private async void OnRuleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = RuleList.SelectedItem as RuleListItem;
        NotifyPropertyChanged(nameof(HasSelection));

        if (_selected is null)
        {
            ClearEditor();
            return;
        }

        var rule = await _service.GetAsync(_selected.Name);
        if (rule is null)
        {
            ClearEditor();
            return;
        }

        LoadEditor(rule);
    }

    private void ClearEditor()
    {
        NameBox.Text = "";
        EnabledToggle.IsOn = true;
        MustContainBox.Text = "";
        MustNotContainBox.Text = "";
        UseRegexToggle.IsOn = false;
        EpisodeFilterBox.Text = "";
        SmartFilterToggle.IsOn = false;
        FeedsList.SelectedItems.Clear();
        TestResultText.Text = "";
    }

    private void LoadEditor(AutoDownloadRule rule)
    {
        NameBox.Text = rule.Name;
        EnabledToggle.IsOn = rule.Enabled;
        MustContainBox.Text = rule.MustContain;
        MustNotContainBox.Text = rule.MustNotContain;
        UseRegexToggle.IsOn = rule.UseRegex;
        EpisodeFilterBox.Text = rule.EpisodeFilter;
        SmartFilterToggle.IsOn = rule.SmartFilter;

        FeedsList.SelectedItems.Clear();
        foreach (var feed in AllFeeds)
        {
            if (rule.AffectedFeeds.Any(u => string.Equals(u, feed.Url, StringComparison.OrdinalIgnoreCase)))
            {
                FeedsList.SelectedItems.Add(feed);
            }
        }

        TestResultText.Text = "";
    }

    private async void OnNewRuleClicked(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "Rule name", PlaceholderText = "My rule" };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "New rule",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = nameBox,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }
        var name = nameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus(InfoBarSeverity.Warning, "Rule name is required.", "");
            return;
        }
        if (await _service.GetAsync(name) is not null)
        {
            ShowStatus(InfoBarSeverity.Warning, "A rule with that name already exists.", name);
            return;
        }
        await _service.UpsertAsync(new AutoDownloadRule { Name = name });
    }

    private async void OnDeleteRuleClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete rule '{_selected.Name}'?",
            Content = "This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await _service.RemoveAsync(_selected.Name);
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus(InfoBarSeverity.Warning, "Rule name is required.", "");
            return;
        }

        // If the user renamed the rule, remove the old entry after the new one lands.
        var oldName = _selected.Name;
        var affected = FeedsList.SelectedItems.OfType<FeedOption>().Select(f => f.Url).ToArray();

        var existing = await _service.GetAsync(oldName);
        var updated = (existing ?? new AutoDownloadRule { Name = name }) with
        {
            Name = name!,
            Enabled = EnabledToggle.IsOn,
            MustContain = MustContainBox.Text ?? "",
            MustNotContain = MustNotContainBox.Text ?? "",
            UseRegex = UseRegexToggle.IsOn,
            EpisodeFilter = EpisodeFilterBox.Text ?? "",
            SmartFilter = SmartFilterToggle.IsOn,
            AffectedFeeds = affected,
        };

        await _service.UpsertAsync(updated);
        if (!string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase))
        {
            await _service.RemoveAsync(oldName);
        }

        _selected = Rules.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        ShowStatus(InfoBarSeverity.Success, "Rule saved.", name!);
    }

    private async void OnRevertClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var rule = await _service.GetAsync(_selected.Name);
        if (rule is not null)
        {
            LoadEditor(rule);
        }
    }

    private void OnTestClicked(object sender, RoutedEventArgs e)
    {
        var title = TestTitleBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(title))
        {
            TestResultText.Text = "";
            return;
        }

        var affected = FeedsList.SelectedItems.OfType<FeedOption>().Select(f => f.Url).ToArray();

        var probe = new AutoDownloadRule
        {
            Name = NameBox.Text ?? "",
            Enabled = EnabledToggle.IsOn,
            MustContain = MustContainBox.Text ?? "",
            MustNotContain = MustNotContainBox.Text ?? "",
            UseRegex = UseRegexToggle.IsOn,
            EpisodeFilter = EpisodeFilterBox.Text ?? "",
            SmartFilter = SmartFilterToggle.IsOn,
            AffectedFeeds = affected,
        };

        // Use a feed URL the probe actually lists, so feed-scoping doesn't block the test.
        var feedUrl = probe.AffectedFeeds.FirstOrDefault() ?? "test://live";
        var result = RuleMatcher.Evaluate(probe, new RssArticle
        {
            FeedUrl = feedUrl,
            Title = title,
            PublishedUtc = DateTime.UtcNow,
        });

        if (result.IsMatch)
        {
            TestResultText.Text = result.NewEpisodeTags.Count > 0
                ? $"✓ Match ({string.Join(", ", result.NewEpisodeTags)})"
                : "✓ Match";
            TestResultText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
        else
        {
            TestResultText.Text = "✗ No match";
            TestResultText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
        }
        else
        {
            Frame?.Navigate(typeof(RssReaderPage));
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void NotifyPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RuleListItem : INotifyPropertyChanged
{
    private string _name = "";
    private bool _enabled = true;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; Raise(nameof(Name)); } }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                Raise(nameof(Enabled));
                Raise(nameof(StateGlyph));
                Raise(nameof(StateBrush));
            }
        }
    }

    public string StateGlyph => _enabled ? "\uE73E" : "\uE711"; // check / block
    public Brush StateBrush => _enabled
        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
        : (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];

    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class FeedOption
{
    public string Url { get; set; } = "";
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<Task> task)
    {
        var tcs = new TaskCompletionSource();
        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await task();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException("DispatcherQueue enqueue failed."));
        }
        return tcs.Task;
    }
}
