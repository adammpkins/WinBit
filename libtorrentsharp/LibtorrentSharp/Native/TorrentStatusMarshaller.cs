using System;
using System.Runtime.InteropServices;

namespace LibtorrentSharp.Native;

internal static class TorrentStatusMarshaller
{
    internal static TorrentStatus GetStatus(IntPtr torrentSessionHandle)
    {
        var ptr = NativeMethods.GetTorrentStatus(torrentSessionHandle);
        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to retrieve torrent status — handle is invalid.");
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeStructs.TorrentStatus>(ptr);
            return new TorrentStatus(native);
        }
        finally
        {
            NativeMethods.FreeTorrentStatus(ptr);
        }
    }
}
