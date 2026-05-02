using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Native;

internal static class TrackerInfoMarshaller
{
    internal static IReadOnlyList<TrackerInfo> GetTrackers(IntPtr torrentSessionHandle)
    {
        NativeMethods.GetTrackers(torrentSessionHandle, out var list);

        try
        {
            if (list.length == 0 || list.items == IntPtr.Zero)
            {
                return Array.Empty<TrackerInfo>();
            }

            var trackers = new List<TrackerInfo>(list.length);
            var entrySize = Marshal.SizeOf<NativeStructs.Tracker>();

            for (var i = 0; i < list.length; i++)
            {
                var entry = Marshal.PtrToStructure<NativeStructs.Tracker>(list.items + entrySize * i);

                var nextAnnounce = entry.next_announce_epoch == 0
                    ? DateTimeOffset.MinValue
                    : DateTimeOffset.FromUnixTimeSeconds(entry.next_announce_epoch);

                trackers.Add(new TrackerInfo(
                    entry.url ?? string.Empty,
                    entry.tier,
                    (TrackerSource)entry.source,
                    entry.verified,
                    entry.scrape_complete,
                    entry.scrape_incomplete,
                    entry.scrape_downloaded,
                    entry.fails,
                    entry.updating,
                    entry.last_error ?? string.Empty,
                    nextAnnounce,
                    entry.trackerid ?? string.Empty,
                    entry.message ?? string.Empty,
                    entry.start_sent,
                    entry.complete_sent,
                    entry.min_announce_epoch == 0
                        ? DateTimeOffset.MinValue
                        : DateTimeOffset.FromUnixTimeSeconds(entry.min_announce_epoch)));
            }

            return trackers;
        }
        finally
        {
            NativeMethods.FreeTrackerList(ref list);
        }
    }
}
