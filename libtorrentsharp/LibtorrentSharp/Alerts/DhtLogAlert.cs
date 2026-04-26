// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Verbose log line emitted by libtorrent's DHT subsystem — things
/// like "node X added to bucket Y", "RPC timeout from peer Z",
/// "traversal X completed in N hops". Session-scoped (DHT is a
/// session-wide service, not per-torrent). <see cref="Module"/>
/// identifies which DHT subsystem emitted the line so consumers can
/// filter (the routing table tends to be much chattier than the RPC
/// manager, for instance).
/// <para>
/// Useful for diagnosing DHT-specific problems — slow lookups, bucket
/// churn, RPC misbehavior — when the structured DHT alerts
/// (<see cref="DhtErrorAlert"/>, <see cref="DhtBootstrapAlert"/>,
/// etc.) don't carry enough detail.
/// </para>
/// <para>
/// <b>Requires opt-in:</b> consumers must include
/// <see cref="LibtorrentSharp.Enums.AlertCategories.DHTLog"/> in
/// <see cref="LibtorrentSessionConfig.AlertCategories"/>. The default
/// <c>RequiredAlertCategories</c> mask intentionally omits DHTLog
/// because the alerts in that category are high-volume and
/// debug-tier — typically only useful when actively debugging DHT
/// behavior.
/// </para>
/// </summary>
public class DhtLogAlert : Alert
{
    internal DhtLogAlert(NativeEvents.DhtLogAlert alert)
        : base(alert.info)
    {
        Module = (DhtModule)alert.module;

        LogMessage = alert.log_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.log_message) ?? string.Empty;
    }

    /// <summary>The DHT subsystem that emitted this log line.</summary>
    public DhtModule Module { get; }

    /// <summary>The DHT log message text emitted by libtorrent's internal logging.</summary>
    public string LogMessage { get; }
}
