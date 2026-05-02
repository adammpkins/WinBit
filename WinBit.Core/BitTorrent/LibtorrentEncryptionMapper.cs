using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

internal static class LibtorrentEncryptionMapper
{
    // libtorrent enc_policy values: 0=forced (require MSE), 1=enabled (prefer MSE
    // but accept plaintext), 2=disabled (plaintext only). Mirror qBittorrent's
    // three-way mapping (see docs/torrent-engine.md → engine alternatives).
    public static int ToPolicy(EncryptionMode mode) => mode switch
    {
        EncryptionMode.Require => 0,
        EncryptionMode.Disable => 2,
        _ => 1, // EncryptionMode.Prefer
    };
}
