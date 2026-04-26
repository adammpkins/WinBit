using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Snapshot of the session's DHT routing-table state, fired in response to
/// <see cref="LibtorrentSession.PostDhtStats"/>. Surfaces the aggregate totals,
/// the per-bucket breakdown (<see cref="Buckets"/>), and the per-lookup
/// metadata for active requests (<see cref="Lookups"/>).
/// </summary>
public class DhtStatsAlert : Alert
{
    internal DhtStatsAlert(NativeEvents.DhtStatsAlert alert)
        : base(alert.info)
    {
        TotalNodes = alert.total_nodes;
        TotalReplacements = alert.total_replacements;
        ActiveRequests = alert.active_requests;
        Buckets = CopyBuckets(alert.buckets, alert.bucket_count);
        Lookups = CopyLookups(alert.lookups, alert.lookup_count);
    }

    /// <summary>
    /// Number of nodes currently in the routing table (sum across all buckets).
    /// 0 on a freshly-started session before any DHT bootstrap completes.
    /// </summary>
    public int TotalNodes { get; }

    /// <summary>
    /// Number of replacement-cache entries (sum across all buckets). Replacement
    /// nodes are candidates that take over when an active node times out.
    /// </summary>
    public int TotalReplacements { get; }

    /// <summary>Outstanding DHT lookups in flight at the time the alert fired.</summary>
    public int ActiveRequests { get; }

    /// <summary>
    /// Per-bucket detail in declaration order (closest first). Empty before any
    /// DHT bootstrap completes. Always non-null.
    /// </summary>
    public IReadOnlyList<DhtRoutingBucket> Buckets { get; }

    /// <summary>
    /// Per-lookup detail for outstanding DHT requests. Empty when no lookups
    /// are in flight (the common case in CI without bootstrapped DHT). Always
    /// non-null. Length equals <see cref="ActiveRequests"/>.
    /// </summary>
    public IReadOnlyList<DhtLookup> Lookups { get; }

    private static IReadOnlyList<DhtRoutingBucket> CopyBuckets(IntPtr nativeBuckets, int count)
    {
        if (nativeBuckets == IntPtr.Zero || count <= 0)
        {
            return Array.Empty<DhtRoutingBucket>();
        }

        var managed = new DhtRoutingBucket[count];
        var stride = Marshal.SizeOf<NativeEvents.DhtRoutingBucketStruct>();
        for (var i = 0; i < count; i++)
        {
            var entryPtr = IntPtr.Add(nativeBuckets, i * stride);
            var entry = Marshal.PtrToStructure<NativeEvents.DhtRoutingBucketStruct>(entryPtr);
            managed[i] = new DhtRoutingBucket(entry.num_nodes, entry.num_replacements, entry.last_active);
        }

        return managed;
    }

    private static IReadOnlyList<DhtLookup> CopyLookups(IntPtr nativeLookups, int count)
    {
        if (nativeLookups == IntPtr.Zero || count <= 0)
        {
            return Array.Empty<DhtLookup>();
        }

        var managed = new DhtLookup[count];
        var stride = Marshal.SizeOf<NativeEvents.DhtLookupStruct>();
        for (var i = 0; i < count; i++)
        {
            var entryPtr = IntPtr.Add(nativeLookups, i * stride);
            var entry = Marshal.PtrToStructure<NativeEvents.DhtLookupStruct>(entryPtr);
            // type points at a libtorrent-owned static literal — Marshal copies
            // the bytes, but we never own or free it.
            var typeLabel = entry.type == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(entry.type) ?? string.Empty;
            managed[i] = new DhtLookup(
                typeLabel,
                new Sha1Hash(entry.target),
                entry.outstanding_requests,
                entry.timeouts,
                entry.responses,
                entry.branch_factor,
                entry.nodes_left,
                entry.last_sent,
                entry.first_timeout);
        }

        return managed;
    }
}
