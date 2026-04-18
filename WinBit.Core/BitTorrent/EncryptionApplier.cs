using Microsoft.Extensions.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Pushes <c>AppSettings.BitTorrent.Encryption</c> through to the engine's
/// <c>AllowedEncryption</c> list. Runs on startup and after every settings edit. Registered
/// after <c>WinBitHostedService</c> so the engine is running on first apply.
/// </summary>
public sealed class EncryptionApplier : IHostedService
{
    private readonly ITorrentSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public EncryptionApplier(ITorrentSessionService session, ISettingsService settings, ILogService log)
    {
        _session = session;
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _settings.Changed += OnSettingsChanged;
        await ApplyAsync(_settings.Current.BitTorrent.Encryption, ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        _ = ApplyAsync(s.BitTorrent.Encryption, CancellationToken.None);

    private async Task ApplyAsync(EncryptionMode mode, CancellationToken ct)
    {
        var result = await _session.SetEncryptionModeAsync(mode, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _log.Write($"Encryption mode apply failed: {result.Error}", LogSeverity.Warning);
        }
    }
}
