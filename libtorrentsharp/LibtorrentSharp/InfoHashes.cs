#nullable enable
using System;

namespace LibtorrentSharp;

/// <summary>
/// Pair of SHA-1 (BitTorrent v1) and SHA-256 (BitTorrent v2) info-hashes for a torrent.
/// Either or both may be present; v1-only is the historical norm, v2-only is rare,
/// hybrid (both) is the migration form.
/// </summary>
public readonly struct InfoHashes : IEquatable<InfoHashes>
{
    public Sha1Hash? V1 { get; }
    public Sha256Hash? V2 { get; }

    public InfoHashes(Sha1Hash? v1, Sha256Hash? v2)
    {
        if (v1 is null && v2 is null)
        {
            throw new ArgumentException("InfoHashes requires at least one of v1 or v2.");
        }
        V1 = v1;
        V2 = v2;
    }

    /// <summary>True when both v1 and v2 hashes are present (hybrid torrent).</summary>
    public bool IsHybrid => V1.HasValue && V2.HasValue;

    /// <summary>
    /// Canonical hex string for the torrent — v1 when present, otherwise v2. Empty
    /// string when both are absent (only possible via <c>default(InfoHashes)</c>).
    /// </summary>
    public string PreferredHex =>
        V1?.ToString() ?? V2?.ToString() ?? string.Empty;

    /// <summary>
    /// Builds <see cref="InfoHashes"/> from the raw 20-byte sha1 and 32-byte sha256
    /// buffers libtorrent surfaces. All-zero buffers are treated as absent.
    /// </summary>
    public static InfoHashes? FromNativeBuffers(ReadOnlySpan<byte> sha1, ReadOnlySpan<byte> sha256)
    {
        Sha1Hash? v1 = sha1.Length == Sha1Hash.ByteLength
            ? new Sha1Hash(sha1) is var candidate1 && candidate1.IsZero ? null : candidate1
            : null;
        Sha256Hash? v2 = sha256.Length == Sha256Hash.ByteLength
            ? new Sha256Hash(sha256) is var candidate2 && candidate2.IsZero ? null : candidate2
            : null;

        if (v1 is null && v2 is null)
        {
            return null;
        }

        return new InfoHashes(v1, v2);
    }

    public bool Equals(InfoHashes other) =>
        Nullable.Equals(V1, other.V1) && Nullable.Equals(V2, other.V2);

    public override bool Equals(object? obj) => obj is InfoHashes h && Equals(h);

    public override int GetHashCode() => HashCode.Combine(V1, V2);

    public override string ToString() =>
        V1 is { } v1 && V2 is { } v2
            ? $"{v1} / {v2}"
            : PreferredHex;

    public static bool operator ==(InfoHashes left, InfoHashes right) => left.Equals(right);

    public static bool operator !=(InfoHashes left, InfoHashes right) => !left.Equals(right);
}
