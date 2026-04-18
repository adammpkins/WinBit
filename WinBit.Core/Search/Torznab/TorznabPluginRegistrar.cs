using Microsoft.Extensions.Hosting;
using WinBit.Core.Networking;
using WinBit.Core.Settings;

namespace WinBit.Core.Search.Torznab;

/// <summary>
/// Keeps the set of Torznab plugins registered with <see cref="ISearchPluginHost"/> in sync with
/// <see cref="SearchSettings.TorznabFeeds"/>. Registers at startup, then re-syncs whenever
/// <see cref="ISettingsService.Changed"/> fires. Identity is the feed <see cref="TorznabFeedDefinition.Name"/>;
/// a name that leaves the desired set is unregistered, a name whose URL / API key / display name
/// changes is unregistered and re-registered so the next search hits the new configuration.
/// </summary>
public sealed class TorznabPluginRegistrar : IHostedService
{
    private readonly ISearchPluginHost _host;
    private readonly ISettingsService _settings;
    private readonly IHttpClientProvider _http;
    private readonly Dictionary<string, TorznabFeedDefinition> _registered =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public TorznabPluginRegistrar(ISearchPluginHost host, ISettingsService settings, IHttpClientProvider http)
    {
        _host = host;
        _settings = settings;
        _http = http;
    }

    public Task StartAsync(CancellationToken ct)
    {
        Sync(_settings.Current.Search.TorznabFeeds);
        _settings.Changed += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    /// <summary>Test hook — runs the diff without going through the settings event.</summary>
    public void Resync() => Sync(_settings.Current.Search.TorznabFeeds);

    private void OnSettingsChanged(object? sender, AppSettings s) => Sync(s.Search.TorznabFeeds);

    private void Sync(IReadOnlyList<TorznabFeedDefinition> desired)
    {
        lock (_gate)
        {
            var want = desired
                .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Url))
                .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var name in _registered.Keys.Except(want.Keys, StringComparer.OrdinalIgnoreCase).ToList())
            {
                _host.Unregister(name);
                _registered.Remove(name);
            }

            foreach (var (name, def) in want)
            {
                if (_registered.TryGetValue(name, out var prev) && SameShape(prev, def))
                {
                    continue;
                }
                if (prev is not null)
                {
                    _host.Unregister(name);
                }
                _host.Register(new TorznabSearchPlugin(def, _http));
                _registered[name] = Clone(def);
            }
        }
    }

    private static bool SameShape(TorznabFeedDefinition a, TorznabFeedDefinition b) =>
        string.Equals(a.Url, b.Url, StringComparison.Ordinal) &&
        string.Equals(a.ApiKey ?? string.Empty, b.ApiKey ?? string.Empty, StringComparison.Ordinal) &&
        string.Equals(a.DisplayName ?? string.Empty, b.DisplayName ?? string.Empty, StringComparison.Ordinal);

    private static TorznabFeedDefinition Clone(TorznabFeedDefinition def) => new()
    {
        Name = def.Name,
        DisplayName = def.DisplayName,
        Url = def.Url,
        ApiKey = def.ApiKey,
        Enabled = def.Enabled,
    };
}
