// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Verbose torrent-scoped log line emitted by libtorrent — things like
/// "tried peer X, got error Y" / "piece N hash failed, redownload from
/// peer Z" / "added piece N to download queue". Useful for diagnosing
/// why a specific torrent is struggling (stalled, failing hashes,
/// dropping peers) when the structured alerts don't carry enough
/// context.
/// <para>
/// Surfaces the same strings the libtorrent log would emit, but
/// scoped to a single torrent rather than session-wide. <see cref="InfoHash"/>
/// is the v1 info-hash — the same identifier the native dispatcher
/// used to route the alert.
/// </para>
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.TorrentLog"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits TorrentLog
/// because the alerts in that category are high-volume and
/// debug-tier — typically only useful when actively debugging a
/// specific stuck torrent.
/// </para>
/// </summary>
public class TorrentLogAlert : Alert
{
    internal TorrentLogAlert(NativeEvents.TorrentLogAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);

        LogMessage = alert.log_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.log_message) ?? string.Empty;
    }

    /// <summary>The torrent the log line was emitted for.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>The v1 info-hash of the torrent — same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The log message text emitted by libtorrent's internal logging.</summary>
    public string LogMessage { get; }
}
