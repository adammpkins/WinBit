namespace WinBit.Core.Networking;

/// <summary>
/// Orchestrates engine-level UPnP / NAT-PMP port mapping. Tracked against
/// <c>AppSettings.Connection.Upnp</c> — a user flip on the Connection settings page routes
/// through <see cref="ISettingsService.Changed"/> into <see cref="ApplyAsync"/>, which in turn
/// updates the engine via <c>ITorrentSessionService.SetPortForwardingAsync</c>.
/// </summary>
public interface IPortForwardingService
{
    bool IsEnabled { get; }

    /// <summary>Pushes <paramref name="enabled"/> into the engine settings.</summary>
    Task<WinBit.Core.Common.Result> ApplyAsync(bool enabled, CancellationToken ct = default);
}
