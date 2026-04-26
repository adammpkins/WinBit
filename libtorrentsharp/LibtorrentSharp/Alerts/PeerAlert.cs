// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
﻿// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

#nullable enable

using System.Net;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Per-peer event raised on a specific torrent — connect, disconnect, ban,
/// snub/unsnub, or generic peer-side error. <see cref="AlertType"/> is the
/// typed discriminator (mirror of libtorrent's per-peer alert family:
/// <c>peer_connect_alert</c>, <c>peer_disconnected_alert</c>, <c>peer_ban_alert</c>,
/// <c>peer_snubbed_alert</c>, <c>peer_unsnubbed_alert</c>, <c>peer_error_alert</c>),
/// so consumers can dispatch on the kind without reflecting on a derived type.
/// Distinct from <see cref="IncomingConnectionAlert"/>, which fires session-scoped
/// the moment the listen socket accepts a connection (before any torrent is known);
/// PeerAlert fires per-torrent after the connection has been associated with one.
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.Peer"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits Peer because
/// these alerts can be high-volume on busy seeds.
/// </para>
/// <para>
/// <see cref="Subject"/> is <c>null</c> for magnet-source torrents whose
/// info-hash is not yet in the session's attached-managers map (same
/// forward-with-null-Subject pattern as the torrent-lifecycle alert family
/// — slices 101–118).
/// </para>
/// </summary>
public class PeerAlert : Alert
{
    internal PeerAlert(NativeEvents.PeerAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
        AlertType = alert.alert_type;
        Address = new IPAddress(alert.v6_address);
        PeerId = (byte[])alert.peer_id.Clone();
    }

    /// <summary>The torrent the peer was associated with at alert time. <c>null</c> for magnet-source torrents not yet in the attached-managers map.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>Info-hash of the torrent this peer event belongs to.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The specific peer-event kind libtorrent raised. See <see cref="PeerAlertType"/> for the full enumeration.</summary>
    public PeerAlertType AlertType { get; }

    /// <summary>
    /// The peer's IP address. Surfaced as-is from libtorrent's
    /// <c>endpoint_address</c> (raw v6 form) — IPv4 peers arrive in v4-mapped
    /// IPv6 form (<c>::ffff:a.b.c.d</c>) and are NOT canonicalized here, unlike
    /// <see cref="LibtorrentSharp.PeerInfo.Address"/> from
    /// <see cref="TorrentHandle.GetPeers"/>. Call
    /// <see cref="IPAddress.MapToIPv4"/> after checking
    /// <see cref="IPAddress.IsIPv4MappedToIPv6"/> if you need the v4 form.
    /// </summary>
    public IPAddress Address { get; }

    /// <summary>
    /// The 20-byte BitTorrent peer ID (BEP 3) the remote peer chose for itself.
    /// Distinct from the torrent's info-hash; the first 8 bytes typically encode
    /// a client signature (e.g. <c>-qB4660-</c> for qBittorrent 4.6.6.0). May be
    /// all-zero if the peer hadn't completed the handshake at alert time.
    /// </summary>
    public byte[] PeerId { get; }
}