// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when received metadata fails validation — malformed info dict,
/// hash mismatch, etc. The torrent continues attempting to fetch fresh
/// metadata from other peers, so this is informational, not terminal.
/// Surfaces <see cref="InfoHash"/> directly (magnet-side handle).
/// </summary>
public class MetadataFailedAlert : Alert
{
    internal MetadataFailedAlert(Native.NativeEvents.MetadataFailedAlert alert)
        : base(alert.info)
    {
        InfoHash = new Sha1Hash(alert.info_hash);
        ErrorCode = alert.error_code;

        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The v1 SHA-1 info-hash of the torrent whose metadata was rejected.</summary>
    public Sha1Hash InfoHash { get; }

    /// <summary>The numeric system / libtorrent error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Human-readable error text.</summary>
    public string ErrorMessage { get; }
}
