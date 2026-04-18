using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.Networking;

/// <summary>
/// Runs as both an <see cref="IHostedService"/> and <see cref="IPortForwardingService"/>. On
/// start, applies the current <c>AppSettings.Connection.Upnp</c> flag to the engine; then
/// re-applies whenever <see cref="ISettingsService.Changed"/> fires. Registered after
/// <c>WinBitHostedService</c> so the engine is running before the first apply.
/// </summary>
public sealed class PortForwardingService : IHostedService, IPortForwardingService
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public bool IsEnabled { get; private set; }

    public PortForwardingService(ITorrentSessionService session, ISettingsService settings, ILogService log)
    {
        _session = session;
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _settings.Changed += OnSettingsChanged;
        await ApplyAsync(_settings.Current.Connection.Upnp, ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    public async Task<Result> ApplyAsync(bool enabled, CancellationToken ct = default)
    {
        IsEnabled = enabled;
        var result = await _session.SetPortForwardingAsync(enabled, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _log.Write($"Port forwarding apply failed: {result.Error}", LogSeverity.Warning);
        }
        return result;
    }

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        _ = ApplyAsync(s.Connection.Upnp, CancellationToken.None);
}
