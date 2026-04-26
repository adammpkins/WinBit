using System;
using System.Net;
using System.Net.Sockets;

namespace LibtorrentSharp.Native;

/// <summary>
/// Shared helper for converting managed <see cref="IPAddress"/> values into the
/// 16-byte v4-mapped v6 representation the C ABI uses for every peer / filter
/// endpoint exchange (see native <c>parse_v6_mapped</c>).
/// </summary>
internal static class AddressMarshal
{
    /// <summary>
    /// Returns a freshly allocated 16-byte buffer containing <paramref name="address"/>
    /// in v4-mapped v6 form (::ffff:a.b.c.d for IPv4, raw 16 bytes for IPv6).
    /// </summary>
    public static byte[] ToV6Mapped(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var result = new byte[16];
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var src = address.GetAddressBytes();
            if (src.Length != 16)
            {
                throw new ArgumentException("IPv6 address must be 16 bytes.", nameof(address));
            }
            Array.Copy(src, result, 16);
            return result;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var src = address.GetAddressBytes();
            if (src.Length != 4)
            {
                throw new ArgumentException("IPv4 address must be 4 bytes.", nameof(address));
            }
            // ::ffff:0:0/96 prefix.
            result[10] = 0xff;
            result[11] = 0xff;
            result[12] = src[0];
            result[13] = src[1];
            result[14] = src[2];
            result[15] = src[3];
            return result;
        }

        throw new ArgumentException($"Unsupported address family: {address.AddressFamily}", nameof(address));
    }
}
