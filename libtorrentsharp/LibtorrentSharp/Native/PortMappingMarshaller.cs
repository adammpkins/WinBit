using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Native;

internal static class PortMappingMarshaller
{
    internal static IReadOnlyList<PortMapping> GetMappings(IntPtr sessionHandle)
    {
        NativeMethods.GetPortMappings(sessionHandle, out var list);

        try
        {
            if (list.length == 0 || list.items == IntPtr.Zero)
            {
                return Array.Empty<PortMapping>();
            }

            var mappings = new List<PortMapping>(list.length);
            var entrySize = Marshal.SizeOf<NativeStructs.PortMappingEntry>();

            for (var i = 0; i < list.length; i++)
            {
                var entry = Marshal.PtrToStructure<NativeStructs.PortMappingEntry>(list.items + entrySize * i);

                mappings.Add(new PortMapping(
                    entry.mapping,
                    entry.external_port,
                    (PortMappingProtocol)entry.protocol,
                    (PortMappingTransport)entry.transport,
                    entry.has_error,
                    entry.error_message ?? string.Empty));
            }

            return mappings;
        }
        finally
        {
            NativeMethods.FreePortMappings(ref list);
        }
    }
}