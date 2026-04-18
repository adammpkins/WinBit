using System.Security.Cryptography;
using System.Text;

namespace WinBit.Core.WebUi;

/// <summary>
/// PBKDF2-HMAC-SHA-256, 100 000 iterations, 32-byte salt, 64-byte derived key — matches
/// qBittorrent 4.6+'s password storage so imported hashes work unchanged. Serialized as
/// <c>"base64(salt):base64(hash)"</c>.
/// </summary>
public static class PasswordHasher
{
    public const int Iterations = 100_000;
    public const int SaltBytes = 32;
    public const int HashBytes = 64;

    public static string Hash(string password)
    {
        if (password is null)
        {
            throw new ArgumentNullException(nameof(password));
        }

        Span<byte> salt = stackalloc byte[SaltBytes];
        RandomNumberGenerator.Fill(salt);
        var derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password),
            salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(derived)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var parts = storedHash.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expected = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password),
            salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(derived, expected);
    }
}
