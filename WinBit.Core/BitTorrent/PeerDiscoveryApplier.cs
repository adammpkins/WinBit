using Microsoft.Extensions.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Pushes DHT / PEX / LSD toggles from <c>AppSettings.BitTorrent</c> into the engine. Runs on
/// startup and on every settings edit. Registered after <c>WinBitHostedService</c> so the
/// engine is running on first apply.
/// </summary>
public sealed class PeerDiscoveryApplier : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public PeerDiscoveryApplier(ITorrentSessionService session, ISettingsService settings, ILogService log)
    {
        _session = session;
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _settings.Changed += OnSettingsChanged;
        await ApplyAsync(_settings.Current, ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        _ = ApplyAsync(s, CancellationToken.None);

    private async Task ApplyAsync(AppSettings settings, CancellationToken ct)
    {
        var bt = settings.BitTorrent;
        var result = await _session.SetPeerDiscoveryAsync(bt.Dht, bt.Pex, bt.Lsd, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _log.Write($"Peer-discovery apply failed: {result.Error}", LogSeverity.Warning);
        }
    }
}
