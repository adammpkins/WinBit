// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when an individual file in a torrent completes — all pieces
/// overlapping the file have passed hash verification. Unlike
/// <see cref="TorrentFinishedAlert"/> (which fires once when the whole
/// torrent completes), this alert fires once per file, so consumers can
/// surface per-file completion in a Content tab or open just-finished
/// files proactively.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.FileProgress"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits FileProgress
/// because the sibling <c>file_progress_alert</c> is high-rate during
/// active downloads and would flood the unmapped-alert fallback path.
/// </para>
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent (MagnetHandle) that finished a file after metadata arrival —
/// magnet handles aren't tracked in the session's TorrentHandle map,
/// so the dispatcher can't resolve a managed TorrentHandle to attribute
/// the file completion to. Callers tracking magnet downloads should use
/// <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class FileCompletedAlert : Alert
{
    internal FileCompletedAlert(NativeEvents.FileCompletedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        FileIndex = alert.file_index;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The torrent whose file completed. May be null for magnet-source completions — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Index of the file that completed — cross-reference with TorrentInfo.Files.</summary>
    public int FileIndex { get; }

    /// <summary>The v1 info-hash of the torrent the completed file belongs to — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}
