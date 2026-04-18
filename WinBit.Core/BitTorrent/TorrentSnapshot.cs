using WinBit.Core.Common;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Per-tick view of a torrent's transient state. <c>StatusPollingLoop</c> captures one of these
/// per <c>TorrentManager</c> every second and batches them into a single <c>TorrentUpdated</c>
/// event so the UI applies all row updates in one dispatcher hop.
/// </summary>
public readonly record struct TorrentSnapshot
{
    public TorrentId Id { get; init; }

    public TorrentState State { get; init; }

    /// <summary>Progress in the range 0..1.</summary>
    public double Progress { get; init; }

    public long BytesDownloaded { get; init; }

    public long BytesUploaded { get; init; }

    public long DownloadSpeedBps { get; init; }

    public long UploadSpeedBps { get; init; }

    public double Ratio { get; init; }

    /// <summary>Null while ETA cannot be estimated (stalled or complete).</summary>
    public TimeSpan? Eta { get; init; }

    public int Seeds { get; init; }

    public int Peers { get; init; }
}
