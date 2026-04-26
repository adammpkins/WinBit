// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

#nullable enable
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired for SSL torrents as a reminder that the torrent won't operate
/// until a valid certificate is supplied via <c>set_ssl_certificate()</c>.
/// The certificate must be signed by the SSL certificate embedded in the
/// <c>.torrent</c> file.
/// <para>
/// <b>Subject may be null</b> when the alert fires for a magnet-source
/// SSL torrent (MagnetHandle) — magnet handles aren't tracked in the
/// session's TorrentHandle map, so the dispatcher can't resolve a
/// managed TorrentHandle to attribute the cert-needed signal to.
/// Callers tracking magnet SSL-cert prompts should use
/// <see cref="InfoHash"/> as the routing key.
/// </para>
/// </summary>
public class TorrentNeedCertAlert : Alert
{
    internal TorrentNeedCertAlert(NativeEvents.TorrentNeedCertAlert alert, TorrentHandle? subject)
        : base(alert.info)
    {
        Subject = subject;
        InfoHash = new Sha1Hash(alert.info_hash);
    }

    /// <summary>The SSL torrent that needs its certificate installed. May be null for magnet-source SSL torrents — see the class summary.</summary>
    public TorrentHandle? Subject { get; }

    /// <summary>The v1 info-hash of the SSL torrent — surfaces the same identifier the native dispatcher used to route the alert.</summary>
    public Sha1Hash InfoHash { get; }
}
