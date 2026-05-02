using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Native;

internal static class PeerInfoMarshaller
{
    internal static IReadOnlyList<PeerInfo> GetPeers(IntPtr torrentSessionHandle)
    {
        NativeMethods.GetPeers(torrentSessionHandle, out var list);

        try
        {
            if (list.length == 0 || list.items == IntPtr.Zero)
            {
                return Array.Empty<PeerInfo>();
            }

            var peers = new List<PeerInfo>(list.length);
            var entrySize = Marshal.SizeOf<NativeStructs.Peer>();

            for (var i = 0; i < list.length; i++)
            {
                var entry = Marshal.PtrToStructure<NativeStructs.Peer>(list.items + entrySize * i);

                var address = new IPAddress(entry.ipv6_address);
                if (address.IsIPv4MappedToIPv6)
                {
                    address = address.MapToIPv4();
                }

                peers.Add(new PeerInfo(
                    address,
                    entry.port,
                    entry.client ?? string.Empty,
                    (PeerFlags)entry.flags,
                    (PeerSource)entry.source,
                    entry.progress,
                    entry.up_rate,
                    entry.down_rate,
                    entry.total_uploaded,
                    entry.total_downloaded,
                    (PeerConnectionType)entry.connection_type,
                    entry.num_hashfails,
                    entry.downloading_piece_index,
                    entry.downloading_block_index,
                    entry.downloading_progress,
                    entry.downloading_total,
                    entry.failcount,
                    entry.payload_up_rate,
                    entry.payload_down_rate,
                    entry.pid ?? Array.Empty<byte>()));
            }

            return peers;
        }
        finally
        {
            NativeMethods.FreePeerList(ref list);
        }
    }
}
