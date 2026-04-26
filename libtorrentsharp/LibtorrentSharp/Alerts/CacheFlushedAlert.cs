// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent's outstanding disk writes have been flushed —
/// either in response to an explicit <c>flush_cache()</c> call, or when a
/// torrent is removed from the session and its pending disk writes
/// complete. A readiness signal that the torrent's files are fully
/// persisted and no longer open.
/// </summary>
public class CacheFlushedAlert : Alert
{
    internal CacheFlushedAlert(NativeEvents.CacheFlushedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
    }

    /// <summary>The torrent whose disk writes have been flushed.</summary>
    public TorrentHandle Subject { get; }
}
