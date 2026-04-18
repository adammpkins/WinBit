using Microsoft.Extensions.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.Networking;

/// <summary>
/// Loads/reloads the PeerGuardian blocklist into <see cref="IIpFilterService"/> whenever
/// <c>AppSettings.Connection.IpFilterEnabled</c> or <c>IpFilterPath</c> changes. Registered as
/// a hosted service so the initial load happens on engine startup.
/// </summary>
public sealed class IpFilterLoader : IHostedService
{
    private readonly IIpFilterService _filter;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    private string? _lastPath;
    private bool _lastEnabled;

    public IpFilterLoader(IIpFilterService filter, ISettingsService settings, ILogService log)
    {
        _filter = filter;
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _settings.Changed += OnSettingsChanged;
        await ReloadAsync(_settings.Current, ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _settings.Changed -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, AppSettings s) =>
        _ = ReloadAsync(s, CancellationToken.None);

    private async Task ReloadAsync(AppSettings s, CancellationToken ct)
    {
        var enabled = s.Connection.IpFilterEnabled;
        var path = s.Connection.IpFilterPath;

        if (enabled == _lastEnabled && string.Equals(path, _lastPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastEnabled = enabled;
        _lastPath = path;

        try
        {
            if (!enabled || string.IsNullOrWhiteSpace(path))
            {
                _filter.Clear();
                _log.Write("IP filter disabled or unconfigured.", LogSeverity.Info);
                return;
            }

            await _filter.LoadAsync(path, ct).ConfigureAwait(false);
            _log.Write($"IP filter loaded: {_filter.RuleCount} ranges from {path}", LogSeverity.Info);
        }
        catch (Exception ex)
        {
            _filter.Clear();
            _log.Write($"IP filter load failed for '{path}': {ex.Message}", LogSeverity.Warning);
        }
    }
}
