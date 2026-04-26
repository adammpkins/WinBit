using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a DHT lookup for a mutable BEP44 item completes successfully.
/// Misses (no peer holds the item) currently fire no alert — they time out
/// internally. <see cref="Data"/> carries the bytes for string-typed entries
/// (the common case); empty for non-string entries until a full <c>entry</c>
/// marshaller lands.
/// </summary>
public class DhtMutableItemAlert : Alert
{
    internal DhtMutableItemAlert(NativeEvents.DhtMutableItemAlert alert)
        : base(alert.info)
    {
        PublicKey = alert.public_key ?? new byte[32];
        Signature = alert.signature ?? new byte[64];
        Seq = alert.seq;
        Salt = CopyBytes(alert.salt, alert.salt_len);
        Data = CopyBytes(alert.data, alert.data_len);
        IsAuthoritative = alert.authoritative != 0;
    }

    /// <summary>The Ed25519 public key the item was stored under (32 bytes).</summary>
    public byte[] PublicKey { get; }

    /// <summary>The Ed25519 signature over <c>seq + salt + bencode(item)</c> (64 bytes).</summary>
    public byte[] Signature { get; }

    /// <summary>BEP44 sequence number for the item.</summary>
    public long Seq { get; }

    /// <summary>The salt used for the lookup. Always non-null; empty when no salt was used.</summary>
    public byte[] Salt { get; }

    /// <summary>
    /// The retrieved bytes for string-typed items. Always non-null; empty for
    /// non-string entries.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>True when libtorrent treats this response as authoritative.</summary>
    public bool IsAuthoritative { get; }

    private static byte[] CopyBytes(IntPtr ptr, int length)
    {
        if (ptr == IntPtr.Zero || length <= 0)
        {
            return Array.Empty<byte>();
        }
        var bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return bytes;
    }
}
