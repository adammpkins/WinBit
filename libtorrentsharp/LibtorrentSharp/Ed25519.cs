using System;
using LibtorrentSharp.Native;

namespace LibtorrentSharp;

/// <summary>
/// Ed25519 helpers backing BEP44 mutable items. Forwards to libtorrent's bundled
/// <c>lt::dht::ed25519_*</c> reference implementation. Provides the bare minimum
/// — seed generation, keypair derivation, signing, and verification — that
/// callers need to author and verify mutable BEP44 payloads. Sizes are fixed by
/// the curve (seed = 32 bytes, public key = 32 bytes, secret key = 64 bytes,
/// signature = 64 bytes).
/// </summary>
public static class Ed25519
{
    /// <summary>Length of an Ed25519 seed, in bytes.</summary>
    public const int SeedSize = 32;

    /// <summary>Length of an Ed25519 public key, in bytes.</summary>
    public const int PublicKeySize = 32;

    /// <summary>Length of an Ed25519 secret key (seed + public key), in bytes.</summary>
    public const int SecretKeySize = 64;

    /// <summary>Length of an Ed25519 signature, in bytes.</summary>
    public const int SignatureSize = 64;

    /// <summary>Generates 32 random bytes suitable as a fresh Ed25519 seed.</summary>
    public static byte[] CreateSeed()
    {
        var seed = new byte[SeedSize];
        NativeMethods.Ed25519CreateSeed(seed);
        return seed;
    }

    /// <summary>
    /// Derives a deterministic Ed25519 keypair from <paramref name="seed"/>.
    /// The same seed always produces the same keypair, so callers that want
    /// long-lived identities should persist the seed (or the secret key).
    /// </summary>
    public static (byte[] PublicKey, byte[] SecretKey) CreateKeypair(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (seed.Length != SeedSize)
        {
            throw new ArgumentException($"Seed must be exactly {SeedSize} bytes.", nameof(seed));
        }

        var publicKey = new byte[PublicKeySize];
        var secretKey = new byte[SecretKeySize];
        NativeMethods.Ed25519CreateKeypair(seed, publicKey, secretKey);
        return (publicKey, secretKey);
    }

    /// <summary>
    /// Signs <paramref name="message"/> with the keypair and returns the
    /// 64-byte signature.
    /// </summary>
    public static byte[] Sign(byte[] message, byte[] publicKey, byte[] secretKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(secretKey);
        if (publicKey.Length != PublicKeySize)
        {
            throw new ArgumentException($"Public key must be exactly {PublicKeySize} bytes.", nameof(publicKey));
        }
        if (secretKey.Length != SecretKeySize)
        {
            throw new ArgumentException($"Secret key must be exactly {SecretKeySize} bytes.", nameof(secretKey));
        }

        var signature = new byte[SignatureSize];
        NativeMethods.Ed25519Sign(message, message.Length, publicKey, secretKey, signature);
        return signature;
    }

    /// <summary>
    /// Verifies that <paramref name="signature"/> is a valid Ed25519 signature
    /// over <paramref name="message"/> for <paramref name="publicKey"/>.
    /// </summary>
    public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publicKey);
        if (signature.Length != SignatureSize)
        {
            throw new ArgumentException($"Signature must be exactly {SignatureSize} bytes.", nameof(signature));
        }
        if (publicKey.Length != PublicKeySize)
        {
            throw new ArgumentException($"Public key must be exactly {PublicKeySize} bytes.", nameof(publicKey));
        }

        return NativeMethods.Ed25519Verify(signature, message, message.Length, publicKey) != 0;
    }
}
