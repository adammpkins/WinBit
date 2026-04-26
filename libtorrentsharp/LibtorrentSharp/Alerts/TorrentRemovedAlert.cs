// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
﻿// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a torrent is removed from the session via
/// <see cref="LibtorrentSession.DetachTorrent(TorrentHandle)"/> (or any
/// overload). <see cref="Subject"/> is the handle that was removed and
/// <see cref="InfoHash"/> is its v1 info-hash — the canonical identifier
/// that survives even when the handle has been invalidated, useful for
/// callers tracking removals without holding a Subject reference.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// torrent removed via <see cref="LibtorrentSession.DetachMagnet"/> —
/// magnet handles aren't tracked in the session's TorrentHandle map, so
/// the dispatcher can't resolve a managed TorrentHandle to attribute
/// the removal to. Magnet-source removals also don't trigger the
/// dispatcher's <c>MarkAsDetached</c> bookkeeping (that's a TorrentHandle
/// concept). Callers tracking magnet removals should use
/// <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TorrentRemovedAlert : Alert
{
    internal TorrentRemovedAlert(NativeEvents.TorrentRemovedAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The handle that was removed; already marked detached by the dispatcher when this alert is delivered. May be null for magnet-source removals — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the removed torrent. Always populated; libtorrent surfaces this from <c>torrent_removed_alert::info_hashes</c> rather than the (now-invalid) handle.</summary>
    public Sha1Hash InfoHash { get; }
}
