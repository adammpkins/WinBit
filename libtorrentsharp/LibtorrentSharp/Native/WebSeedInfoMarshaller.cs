using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LibtorrentSharp.Native;

internal static class WebSeedInfoMarshaller
{
    internal static IReadOnlyList<WebSeedInfo> GetWebSeeds(IntPtr torrentHandle)
    {
        NativeMethods.GetWebSeeds(torrentHandle, out var list);

        try
        {
            if (list.count == 0 || list.items == IntPtr.Zero)
            {
                return Array.Empty<WebSeedInfo>();
            }

            var seeds = new List<WebSeedInfo>(list.count);
            var entrySize = Marshal.SizeOf<NativeStructs.WebSeed>();

            for (var i = 0; i < list.count; i++)
            {
                var entry = Marshal.PtrToStructure<NativeStructs.WebSeed>(list.items + entrySize * i);
                seeds.Add(new WebSeedInfo { Url = entry.url ?? string.Empty });
            }

            return seeds;
        }
        finally
        {
            NativeMethods.FreeWebSeedList(ref list);
        }
    }
}
