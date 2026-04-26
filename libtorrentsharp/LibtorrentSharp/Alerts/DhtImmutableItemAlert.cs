using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a DHT lookup for an immutable BEP44 item completes successfully.
/// Misses (no peer holds the item) currently fire no alert — they time out
/// internally. <see cref="Data"/> carries the bytes for string-typed items
/// (the common case for items stored via
/// <see cref="LibtorrentSession.DhtPutImmutable"/>); empty for other entry
/// types until a full <c>entry</c> marshaller lands.
/// </summary>
public class DhtImmutableItemAlert : Alert
{
    internal DhtImmutableItemAlert(NativeEvents.DhtImmutableItemAlert alert)
        : base(alert.info)
    {
        Target = new Sha1Hash(alert.target);

        if (alert.data == IntPtr.Zero || alert.length <= 0)
        {
            Data = Array.Empty<byte>();
        }
        else
        {
            var bytes = new byte[alert.length];
            Marshal.Copy(alert.data, bytes, 0, alert.length);
            Data = bytes;
        }
    }

    /// <summary>The DHT target the item was retrieved from.</summary>
    public Sha1Hash Target { get; }

    /// <summary>
    /// The retrieved bytes for string-typed items. Always non-null; empty for
    /// non-string entries (rare in practice for items put via
    /// <see cref="LibtorrentSession.DhtPutImmutable"/>).
    /// </summary>
    public byte[] Data { get; }
}
