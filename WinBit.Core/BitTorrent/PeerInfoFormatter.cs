using System.Text;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Pure formatting helpers for <see cref="PeerInfo"/> fields. Extracted to Core so the
/// display logic can be covered by unit tests without a WinUI project reference.
/// </summary>
public static class PeerInfoFormatter
{
    /// <summary>
    /// Builds the compact flags string shown in the Peers tab Flags column, following the same
    /// character order as qBittorrent's determineFlags(). "K?" is the normal output for a peer
    /// that is connected but neither side is actively transferring — both choke states are false
    /// (unchoked) by default, and neither side has declared interest.
    /// </summary>
    public static string BuildFlags(PeerInfo info)
    {
        var sb = new StringBuilder();

        // D/d — we want pieces; D = they haven't choked us, d = they have
        if (info.IsInteresting && !info.IsRemoteChoked)
            sb.Append('D');
        else if (info.IsInteresting && info.IsRemoteChoked)
            sb.Append('d');
        // K — we haven't choked the peer, but we don't want its pieces
        else if (!info.IsChoked)
            sb.Append('K');

        // U/u — peer wants pieces; U = we haven't choked it, u = we have
        if (info.IsRemoteInteresting && !info.IsChoked)
            sb.Append('U');
        else if (info.IsRemoteInteresting && info.IsChoked)
            sb.Append('u');
        // ? — the peer hasn't choked us, but it doesn't want our pieces
        else if (!info.IsRemoteChoked)
            sb.Append('?');

        if (info.IsOptimisticUnchoke)   sb.Append('O');
        if (info.IsSnubbed)             sb.Append('S');
        if (info.IsIncomingConnection)  sb.Append('I');
        if (info.IsFromDht)             sb.Append('H');
        if (info.IsFromPex)             sb.Append('X');
        if (info.IsFromLsd)             sb.Append('L');

        // E = full RC4 stream encryption; e = MSE plaintext (handshake only)
        if (info.IsEncrypted)           sb.Append('E');
        if (info.IsHandshakeEncrypted)  sb.Append('e');

        if (info.IsUtp)                 sb.Append('P');
        if (info.IsHolepunched)         sb.Append('h');

        return sb.Length > 0 ? sb.ToString() : "—";
    }

    public static string FormatSpeed(long bps)
    {
        if (bps <= 0) return "—";
        return FormatBytes(bps) + "/s";
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B",
    };
}
