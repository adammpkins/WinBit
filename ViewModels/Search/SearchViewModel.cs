using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.Search;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Search;

/// <summary>
/// Drives the Search page. Kicks off an <see cref="ISearchPluginHost.SearchAsync"/> stream,
/// marshals each incoming hit to the UI via <see cref="IDispatcherQueueProvider"/>, and tracks
/// IsSearching so the progress ring can show/hide. A new query cancels the previous one.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly ISearchPluginHost _host;
    private readonly IDispatcherQueueProvider _dispatcher;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private int resultCount;

    [ObservableProperty]
    private string? statusText;

    public ObservableCollection<SearchHitRowViewModel> Hits { get; } = new();

    public ObservableCollection<SearchPluginToggle> PluginToggles { get; } = new();

    public SearchViewModel(ISearchPluginHost host, IDispatcherQueueProvider dispatcher)
    {
        _host = host;
        _dispatcher = dispatcher;
        RefreshPlugins();
    }

    public void RefreshPlugins()
    {
        PluginToggles.Clear();
        foreach (var plugin in _host.Plugins)
        {
            PluginToggles.Add(new SearchPluginToggle(plugin.Name, plugin.DisplayName));
        }
    }

    public async Task RunAsync()
    {
        var text = (Query ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Hits.Clear();
        ResultCount = 0;
        StatusText = null;
        IsSearching = true;

        var active = PluginToggles.Where(p => p.Enabled).Select(p => p.Name).ToArray();
        var names = active.Length == PluginToggles.Count ? null : active;

        try
        {
            await foreach (var hit in _host.SearchAsync(new SearchRequest(text), names, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _dispatcher.Enqueue(() =>
                {
                    Hits.Add(new SearchHitRowViewModel(hit));
                    ResultCount = Hits.Count;
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _dispatcher.Enqueue(() =>
            {
                IsSearching = false;
                if (Hits.Count == 0)
                {
                    StatusText = PluginToggles.Count == 0
                        ? "No search providers configured. Add a Torznab/Jackett endpoint in SearchSettings.TorznabFeeds."
                        : $"No results for \"{text}\".";
                }
            });
        }
    }

    public void Cancel() => _cts?.Cancel();
}

public sealed partial class SearchPluginToggle : ObservableObject
{
    public SearchPluginToggle(string name, string displayName)
    {
        Name = name;
        DisplayName = displayName;
    }

    public string Name { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool enabled = true;
}
