#nullable enable
using System;
using System.Buffers.Binary;

namespace LibtorrentSharp;

/// <summary>
/// 32-byte SHA-256 digest. Backs the BitTorrent v2 info-hash. Same design as
/// <see cref="Sha1Hash"/> — four inline ulong fields, allocation-free equality.
/// </summary>
public readonly struct Sha256Hash : IEquatable<Sha256Hash>
{
    /// <summary>Length in bytes of a SHA-256 digest.</summary>
    public const int ByteLength = 32;

    /// <summary>Length in chars of the hex string representation.</summary>
    public const int HexLength = 64;

    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    /// <summary>
    /// Builds a hash from a 32-byte buffer. Throws if the span is the wrong length.
    /// </summary>
    public Sha256Hash(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException(
                $"Sha256Hash requires exactly {ByteLength} bytes; got {bytes.Length}.",
                nameof(bytes));
        }

        _a = BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
        _b = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8, 8));
        _c = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16, 8));
        _d = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24, 8));
    }

    /// <summary>Whether this hash is the all-zero sentinel libtorrent uses for "absent".</summary>
    public bool IsZero => _a == 0 && _b == 0 && _c == 0 && _d == 0;

    /// <summary>Writes the hash into a freshly-allocated 32-byte array.</summary>
    public byte[] ToArray()
    {
        var result = new byte[ByteLength];
        WriteTo(result);
        return result;
    }

    /// <summary>Writes the hash into <paramref name="destination"/> (length must be ≥ 32).</summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < ByteLength)
        {
            throw new ArgumentException(
                $"Destination is {destination.Length} bytes; needs ≥ {ByteLength}.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], _a);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8, 8), _b);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(16, 8), _c);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(24, 8), _d);
    }

    /// <summary>
    /// Parses a 64-char hex string. Returns false on length mismatch or non-hex chars.
    /// Case-insensitive; canonical <see cref="ToString"/> output is lowercase.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> hex, out Sha256Hash hash)
    {
        hash = default;
        if (hex.Length != HexLength)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[ByteLength];
        for (var i = 0; i < ByteLength; i++)
        {
            if (!TryHex(hex[i * 2], out var hi) || !TryHex(hex[i * 2 + 1], out var lo))
            {
                return false;
            }
            bytes[i] = (byte)((hi << 4) | lo);
        }

        hash = new Sha256Hash(bytes);
        return true;
    }

    private static bool TryHex(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
        return value >= 0;
    }

    public override string ToString() => Convert.ToHexString(ToArray()).ToLowerInvariant();

    public bool Equals(Sha256Hash other) =>
        _a == other._a && _b == other._b && _c == other._c && _d == other._d;

    public override bool Equals(object? obj) => obj is Sha256Hash h && Equals(h);

    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);

    public static bool operator ==(Sha256Hash left, Sha256Hash right) => left.Equals(right);

    public static bool operator !=(Sha256Hash left, Sha256Hash right) => !left.Equals(right);
}
