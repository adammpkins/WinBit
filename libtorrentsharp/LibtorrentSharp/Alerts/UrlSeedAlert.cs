// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.

using System.Runtime.InteropServices;
using LibtorrentSharp.Native;

namespace LibtorrentSharp.Alerts;

/// <summary>
/// Fired when an HTTP / web-seed (BEP 17 / BEP 19) lookup or response
/// fails. <see cref="ServerUrl"/> identifies the failing seed URL;
/// <see cref="ErrorMessage"/> carries either libtorrent's error text or
/// the server-sent message; <see cref="ErrorCode"/> is libtorrent's
/// numeric <c>error_code::value()</c> — zero when libtorrent itself did
/// not error and the failure was a server-sent message instead.
/// <para>
/// Routes via info-hash through the attached-handle map; emitted under
/// the <see cref="LibtorrentSharp.Enums.AlertCategories.Error"/>
/// category, which is part of the binding's default required mask, so
/// no opt-in is needed.
/// </para>
/// </summary>
public class UrlSeedAlert : Alert
{
    internal UrlSeedAlert(NativeEvents.UrlSeedAlert alert, TorrentHandle subject)
        : base(alert.info)
    {
        Subject = subject;
        ErrorCode = alert.error_code;

        ServerUrl = alert.server_url == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.server_url) ?? string.Empty;
        ErrorMessage = alert.error_message == default
            ? string.Empty
            : Marshal.PtrToStringUTF8(alert.error_message) ?? string.Empty;
    }

    /// <summary>The torrent whose web seed failed.</summary>
    public TorrentHandle Subject { get; }

    /// <summary>Numeric libtorrent error code; zero when the failure was a server-sent message rather than a transport / parse error.</summary>
    public int ErrorCode { get; }

    /// <summary>The web-seed URL whose lookup or response failed.</summary>
    public string ServerUrl { get; }

    /// <summary>Human-readable error text — libtorrent's message when <see cref="ErrorCode"/> is non-zero, the server-sent body otherwise.</summary>
    public string ErrorMessage { get; }
}
