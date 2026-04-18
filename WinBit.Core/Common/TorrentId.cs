namespace WinBit.Core.Common;

/// <summary>
/// Opaque identifier for a torrent in the session. Backed by the info-hash (v1 SHA-1 or v2 SHA-256
/// depending on the torrent). Never display to users — see <see cref="TorrentHandle"/> for name.
/// </summary>
public readonly record struct TorrentId(string Value)
{
    public override string ToString() => Value;

    public static TorrentId FromInfoHash(string infoHashHex) =>
        string.IsNullOrWhiteSpace(infoHashHex)
            ? throw new ArgumentException("Info-hash may not be empty.", nameof(infoHashHex))
            : new TorrentId(infoHashHex.ToLowerInvariant());
}
