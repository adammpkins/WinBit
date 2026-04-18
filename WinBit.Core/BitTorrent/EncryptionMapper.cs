using MonoTorrent.Connections;
using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Translates <see cref="EncryptionMode"/> to the MonoTorrent <see cref="EncryptionType"/>
/// list the engine expects. RC4 variants come first so the engine prefers encrypted
/// handshakes when the peer offers a choice.
/// </summary>
public static class EncryptionMapper
{
    public static List<EncryptionType> ToMonoTorrent(EncryptionMode mode) => mode switch
    {
        EncryptionMode.Prefer => new List<EncryptionType> { EncryptionType.RC4Full, EncryptionType.RC4Header, EncryptionType.PlainText },
        EncryptionMode.Require => new List<EncryptionType> { EncryptionType.RC4Full, EncryptionType.RC4Header },
        EncryptionMode.Disable => new List<EncryptionType> { EncryptionType.PlainText },
        _ => new List<EncryptionType> { EncryptionType.RC4Full, EncryptionType.RC4Header, EncryptionType.PlainText },
    };
}
