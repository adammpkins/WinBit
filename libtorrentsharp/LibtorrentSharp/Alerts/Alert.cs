// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using System;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Base class for every event raised by libtorrent during a session's lifetime
/// — torrent state changes, peer connections, tracker announces, DHT activity,
/// disk-storage events, performance warnings, etc. Mirrors libtorrent's
/// <c>lt::alert</c>. Surfaced through <see cref="LibtorrentSession.Alerts"/>
/// (an <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> fed by the
/// internal alert pump).
/// <para>
/// Consumers typically pattern-match on the concrete subclass to extract
/// typed fields:
/// <code>
/// await foreach (var a in session.Alerts.WithCancellation(ct))
/// {
///     switch (a)
///     {
///         case TorrentStatusAlert s: /* OldState/NewState */ break;
///         case TrackerErrorAlert t: /* TrackerUrl/ErrorCode */ break;
///         // ... ~50 concrete subclasses total
///     }
/// }
/// </code>
/// Unmapped libtorrent alert types still arrive as a raw <see cref="Alert"/>
/// instance with <see cref="Type"/> set so consumers can correlate via the
/// numeric discriminator while a typed subclass is added in a future slice.
/// </para>
/// <para>
/// Inherits <see cref="EventArgs"/> for back-compat with the pre-Phase-E
/// <c>event EventHandler&lt;SessionAlert&gt;</c> dispatch path.
/// </para>
/// </summary>
public class Alert : EventArgs
{
    internal Alert(NativeEvents.AlertBase alert)
    {
        Type = alert.type;
        Category = alert.category;
        Timestamp = DateTimeOffset.FromUnixTimeSeconds(alert.timestamp);
        Message = alert.message;
    }

    /// <summary>
    /// Numeric discriminator naming the underlying libtorrent alert
    /// (mirrors <c>alert::type()</c>). Stable across the C ABI for round-trip
    /// correlation. Useful for telemetry / logging keyed by alert kind, and
    /// as a fallback when a libtorrent alert type isn't yet mapped to a
    /// concrete <see cref="Alert"/> subclass.
    /// </summary>
    public AlertType Type { get; }

    /// <summary>
    /// Bitmask of <see cref="AlertCategories"/> flags this alert belongs to
    /// (mirrors <c>alert::category()</c>). The session's configured
    /// <c>RequiredAlertCategories</c> mask filters which categories are
    /// queued — alerts whose category bits don't intersect the mask never
    /// surface. Some categories (e.g. <c>AlertCategories.Peer</c>) are
    /// high-volume and intentionally opt-in.
    /// </summary>
    public int Category { get; }

    /// <summary>
    /// Wall-clock time the alert was queued by libtorrent (one-second
    /// resolution — libtorrent emits Unix timestamps, not <c>time_point</c>
    /// values). Use this to order alerts across categories rather than the
    /// arrival order on the consumer side, which can be batched.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Human-readable rendering of the alert produced by libtorrent's
    /// <c>alert::message()</c>. Format and content are not stable across
    /// libtorrent versions — for programmatic decisions, prefer typed fields
    /// on the concrete subclass over parsing this string.
    /// </summary>
    public string Message { get; }
}