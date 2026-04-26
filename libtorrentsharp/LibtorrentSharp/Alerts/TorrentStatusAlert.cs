// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
﻿// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired on every torrent state-machine transition (e.g. CheckingFiles →
/// Downloading, Downloading → Seeding). <see cref="Subject"/> is the
/// handle whose state changed, <see cref="OldState"/> / <see cref="NewState"/>
/// describe the transition, and <see cref="InfoHash"/> is the v1
/// info-hash — the same identifier the native dispatcher used to route
/// the alert.
/// </summary>
public class TorrentStatusAlert : Alert
{
    internal TorrentStatusAlert(NativeEvents.TorrentStatusAlert alert, TorrentHandle subjectManager)
        : base(alert.info)
    {
        Subject = subjectManager;
        OldState = alert.old_state;
        NewState = alert.new_state;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The handle whose state-machine transition triggered this alert. The native dispatcher resolves this from the alert's torrent association before the typed alert lands on the consumer side.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>The state libtorrent was reporting on the previous status pump — the "from" side of the transition. See <see cref="TorrentState"/> for the lifecycle phases (CheckingFiles → CheckingResume → DownloadingMetadata → Downloading → Finished/Seeding).</summary>
    public TorrentState OldState { get; }

    /// <summary>The state libtorrent transitioned into — the "to" side of the transition. Most consumer code keys on this value alone (e.g. badge updates); pair with <see cref="OldState"/> only when the transition direction matters (e.g. detecting Downloading → Finished as the "completion" event).</summary>
    public TorrentState NewState { get; }

    /// <summary>The v1 info-hash of the torrent whose state changed — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}