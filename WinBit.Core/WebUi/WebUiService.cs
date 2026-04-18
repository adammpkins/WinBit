using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
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
    private readonly ILogService _log;
    private WebApplication? _app;
    private int? _boundPort;

    public bool IsRunning => _app is not null;

    public int? BoundPort => _boundPort;

    public WebUiService(ISettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_settings.Current.WebUi.Enabled)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder();

        // Route Kestrel's logs through our ILogService so the Logs tab picks up request noise
        // without pulling in a second logging system.
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogServiceLoggerProvider(_log));

        var port = _settings.Current.WebUi.Port;
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(Math.Max(0, port));
        });

        var app = builder.Build();
        app.MapGet("/api/v2/app/version", () => Results.Text(VersionString));

        await app.StartAsync(ct).ConfigureAwait(false);

        _boundPort = ResolveBoundPort(app);
        _app = app;

        _log.Write($"Web UI listening on port {_boundPort?.ToString() ?? "?"}.");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_app is null)
        {
            return;
        }
        await _app.StopAsync(ct).ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
        _boundPort = null;
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
