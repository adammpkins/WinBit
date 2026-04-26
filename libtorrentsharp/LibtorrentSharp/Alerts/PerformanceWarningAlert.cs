// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
﻿// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when libtorrent detects a performance issue that may degrade
/// throughput — disk write queue full, send buffer congestion, too many
/// outstanding piece requests, etc. <see cref="WarningCode"/> is a typed
/// discriminator (mirror of libtorrent's <c>performance_alert::performance_warning_t</c>)
/// identifying which subsystem flagged the warning, so consumers can
/// surface targeted advice (e.g. "increase send_buffer_watermark" for
/// SendBufferWatermark warnings).
/// <para>
/// Session-scoped: the alert isn't tied to a specific torrent — the
/// warning condition is observed at the session/disk/IO layer. For
/// torrent-scoped diagnostics use <see cref="TorrentLogAlert"/> (slice
/// 74) or <see cref="TorrentErrorAlert"/> instead.
/// </para>
/// <para>
/// Fires under the <c>performance_warning</c> alert category, which is
/// in <c>RequiredAlertCategories</c> by default — no opt-in needed.
/// Volume is low (warnings are infrequent), unlike the explicit-opt-in
/// log families (<c>TorrentLog</c> / <c>SessionLog</c> / <c>DHTLog</c>).
/// </para>
/// </summary>
public class PerformanceWarningAlert : Alert
{
    internal PerformanceWarningAlert(NativeEvents.PerformanceWarningAlert alert)
        : base(alert.info)
    {
        WarningCode = alert.warning_code;
    }

    /// <summary>The specific performance condition libtorrent flagged. See <see cref="PerformanceWarningType"/> for the full enumeration.</summary>
    public PerformanceWarningType WarningCode { get; }
}
