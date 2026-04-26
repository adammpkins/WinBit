using System;
using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when a DHT put operation completes (immutable or mutable BEP44 item).
/// For immutable puts only <see cref="Target"/> + <see cref="NumSuccess"/> are
/// populated; the mutable-side fields read as zero / empty. For mutable puts
/// (lands in a follow-up slice) the BEP44 envelope (<see cref="PublicKey"/>,
/// <see cref="Signature"/>, <see cref="Salt"/>, <see cref="Seq"/>) is filled in.
/// </summary>
public class DhtPutAlert : Alert
{
    internal DhtPutAlert(NativeEvents.DhtPutAlert alert)
        : base(alert.info)
    {
        Target = new Sha1Hash(alert.target);
        NumSuccess = alert.num_success;
        PublicKey = alert.public_key ?? new byte[32];
        Signature = alert.signature ?? new byte[64];
        Seq = alert.seq;
        Salt = CopyBytes(alert.salt, alert.salt_len);
    }

    /// <summary>
    /// The DHT target the data was stored under. For an immutable item this is
    /// <c>SHA1(bencode(data))</c>, identical to the value returned by
    /// <see cref="LibtorrentSession.DhtPutImmutable"/>. For a mutable item it
    /// is <c>SHA1(public_key + salt)</c>.
    /// </summary>
    public Sha1Hash Target { get; }

    /// <summary>
    /// Number of DHT peers that accepted the put. Will be 0 when the network
    /// hasn't bootstrapped yet or when no contacted node has spare storage.
    /// </summary>
    public int NumSuccess { get; }

    /// <summary>Ed25519 public key (32 bytes). All zeros for immutable puts.</summary>
    public byte[] PublicKey { get; }

    /// <summary>Ed25519 signature (64 bytes). All zeros for immutable puts.</summary>
    public byte[] Signature { get; }

    /// <summary>BEP44 sequence number. <c>0</c> for immutable puts.</summary>
    public long Seq { get; }

    /// <summary>Mutable-item salt. Always non-null; empty for immutable puts.</summary>
    public byte[] Salt { get; }

    /// <summary>True when this alert represents a mutable BEP44 put (any non-zero public-key byte).</summary>
    public bool IsMutable
    {
        get
        {
            for (var i = 0; i < PublicKey.Length; i++)
            {
                if (PublicKey[i] != 0) return true;
            }
            return false;
        }
    }

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
