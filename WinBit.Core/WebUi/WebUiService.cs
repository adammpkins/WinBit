using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Rss;
using WinBit.Core.Search;
using WinBit.Core.Settings;
using WinBit.Core.Tags;
using WinBit.Core.WebUi.Endpoints;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace WinBit.Core.WebUi;

/// <summary>
/// Starts / stops an in-process Kestrel <see cref="WebApplication"/> when
/// <see cref="WebUiSettings.Enabled"/> is true. Binds to <see cref="WebUiSettings.Port"/>
/// on every local interface; port 0 lets the OS pick an ephemeral port so integration tests
/// can coexist on the same machine. Controllers for the qBittorrent-compatible API ship in
/// later M10 deliverables; this host currently only serves a single version endpoint so
/// clients can detect WinBit and tests can prove the host is alive.
/// </summary>
public sealed class WebUiService : IHostedService, IWebUiService, IAsyncDisposable
{
    public const string VersionString = "WinBit/0.1.0";

    private readonly ISettingsService _settings;
    private readonly IWebUiAuthService _auth;
    private readonly ITorrentSessionService _session;
    private readonly ILogService _log;
    private readonly IPeerLogService _peerLog;
    private readonly ICategoryService _categories;
    private readonly ITagService _tags;
    private readonly IRssService _rss;
    private readonly IAutoDownloaderService _autoDownloader;
    private readonly IRssArticleCache _rssArticles;
    private readonly IRssRefresher _rssRefresher;
    private readonly ITorrentCreatorQueue _creatorQueue;
    private readonly ITorrentStateStore _stateStore;
    private readonly Paths _paths;
    private readonly ISearchPluginHost _searchPluginHost;
    private WebApplication? _app;
    private int? _boundPort;
    private string? _currentBindAddress;
    private int? _currentPort;
    private int _restartPending;                        // 0/1 via Interlocked
    private EventHandler<AppSettings>? _settingsWatcher;

    public bool IsRunning => _app is not null;

    public int? BoundPort => _boundPort;

    public WebUiService(ISettingsService settings, IWebUiAuthService auth,
        ITorrentSessionService session, ILogService log, IPeerLogService peerLog,
        ICategoryService categories, ITagService tags,
        IRssService rss, IAutoDownloaderService autoDownloader, IRssArticleCache rssArticles,
        IRssRefresher rssRefresher, ITorrentCreatorQueue creatorQueue,
        ITorrentStateStore stateStore, Paths paths, ISearchPluginHost searchPluginHost)
    {
        _settings = settings;
        _auth = auth;
        _session = session;
        _log = log;
        _peerLog = peerLog;
        _categories = categories;
        _tags = tags;
        _rss = rss;
        _autoDownloader = autoDownloader;
        _rssArticles = rssArticles;
        _rssRefresher = rssRefresher;
        _creatorQueue = creatorQueue;
        _stateStore = stateStore;
        _paths = paths;
        _searchPluginHost = searchPluginHost;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Always subscribe to settings so we can react to the toggle being
        // flipped on later — even if we're disabled at host-start time.
        if (_settingsWatcher is null)
        {
            _settingsWatcher = OnSettingsChanged;
            _settings.Changed += _settingsWatcher;
        }

        if (!_settings.Current.WebUi.Enabled)
        {
            return;
        }

        await StartHostAsync(ct).ConfigureAwait(false);
    }

    private async Task StartHostAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        // Route Kestrel's logs through our ILogService so the Logs tab picks up request noise
        // without pulling in a second logging system.
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogServiceLoggerProvider(_log));

        var settings = _settings.Current.WebUi;
        var port = settings.Port;
        var useHttps = settings.Https;
        var cert = useHttps ? WebUiCertificateProvider.Resolve(settings, _paths) : null;
        var bindAddress = settings.EnableRemoteAccess ? "0.0.0.0" : settings.BindAddress;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Parse(bindAddress), Math.Max(0, port), listen =>
            {
                if (cert is not null)
                {
                    listen.UseHttps(cert);
                }
            });
        });

        var app = builder.Build();
        // WinBit Vue SPA — catch-all after API endpoints.
        WinBitAppAssets.Map(app);

        AppEndpoints.Map(app, _settings, _auth);
        AuthEndpoints.Map(app, _auth);
        TorrentsEndpoints.Map(app, _session, _auth, _settings, _stateStore);
        TransferEndpoints.Map(app, _session, _auth, _settings);
        LogEndpoints.Map(app, _log, _peerLog, _auth);
        SyncEndpoints.Map(app, _session, _settings, _categories, _tags, _auth);
        RssEndpoints.Map(app, _rss, _autoDownloader, _rssArticles, _rssRefresher, _auth);
        TorrentCreatorEndpoints.Map(app, _creatorQueue, _auth);
        SearchEndpoints.Map(app, _auth, _searchPluginHost);

        await app.StartAsync(ct).ConfigureAwait(false);

        _boundPort = ResolveBoundPort(app);
        _currentBindAddress = bindAddress;
        _currentPort = port;
        _app = app;

        _log.Write($"Web UI listening on {bindAddress}:{_boundPort?.ToString() ?? "?"}.");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_settingsWatcher is not null)
        {
            _settings.Changed -= _settingsWatcher;
            _settingsWatcher = null;
        }

        await StopHostAsync(ct).ConfigureAwait(false);
    }

    private async Task StopHostAsync(CancellationToken ct)
    {
        if (_app is null) return;

        await _app.StopAsync(ct).ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
        _boundPort = null;
        _currentBindAddress = null;
        _currentPort = null;
        _log.Write("Web UI stopped.");
    }

    private void OnSettingsChanged(object? sender, AppSettings s)
    {
        var enabled = s.WebUi.Enabled;
        var running = _app is not null;

        // Toggle off → stop. Toggle on → start. Already-running rebind/port
        // changes restart in place. Disabled-and-already-stopped is a no-op.
        if (!enabled && !running) return;
        if (enabled && running)
        {
            var newBind = s.WebUi.EnableRemoteAccess ? "0.0.0.0" : s.WebUi.BindAddress;
            var newPort = s.WebUi.Port;
            if (newBind == _currentBindAddress && newPort == _currentPort) return;
        }

        if (Interlocked.Exchange(ref _restartPending, 1) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Let the HTTP response that triggered this change finish before
                // Kestrel tears down (only matters for in-place restart, but the
                // small delay is harmless on cold start/stop too).
                await Task.Delay(500).ConfigureAwait(false);
                await StopHostAsync(CancellationToken.None).ConfigureAwait(false);
                if (enabled)
                {
                    await StartHostAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _restartPending, 0);
            }
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private static int? ResolveBoundPort(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addressesFeature = server.Features.Get<IServerAddressesFeature>();
        var address = addressesFeature?.Addresses.FirstOrDefault();
        if (address is null)
        {
            return null;
        }
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return uri.Port;
        }
        return null;
    }
}

/// <summary>Minimal ILoggerProvider that relays ASP.NET Core logs into <see cref="ILogService"/>.</summary>
internal sealed class LogServiceLoggerProvider : ILoggerProvider
{
    private readonly ILogService _log;

    public LogServiceLoggerProvider(ILogService log) => _log = log;

    public ILogger CreateLogger(string categoryName) => new Relay(_log, categoryName);

    public void Dispose() { }

    private sealed class Relay : ILogger
    {
        private readonly ILogService _log;
        private readonly string _category;

        public Relay(ILogService log, string category)
        {
            _log = log;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var severity = logLevel >= LogLevel.Error ? LogSeverity.Critical : LogSeverity.Warning;
            _log.Write($"WebUI [{_category}]: {formatter(state, exception)}", severity);
        }
    }
}
