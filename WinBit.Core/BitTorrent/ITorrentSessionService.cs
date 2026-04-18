using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Wraps the BitTorrent engine (MonoTorrent). Full surface arrives in M3; M1 defines the contract.
/// </summary>
public interface ITorrentSessionService : IAsyncDisposable
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    IReadOnlyList<TorrentId> Torrents { get; }
}
