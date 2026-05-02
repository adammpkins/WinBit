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
/// Fires under the <c>performance_warning</c> alert category
/// (<see cref="LibtorrentSharp.Enums.AlertCategories.PerformanceWarning"/>
/// = <c>1 &lt;&lt; 9</c>). This category is <strong>not</strong> in
/// <c>RequiredAlertCategories</c> — callers must explicitly include
/// <c>AlertCategories.PerformanceWarning</c> in the session's
/// <c>alert_mask</c> (via <c>SettingsPack.Set("alert_mask", ...)</c>)
/// to receive these alerts. Volume is low (warnings are infrequent);
/// opt in per-session where performance diagnostics are needed.
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
