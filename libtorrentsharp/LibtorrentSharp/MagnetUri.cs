#nullable enable
using System;

namespace LibtorrentSharp;

/// <summary>
/// Convenience parser for BEP-9 magnet URIs. Validates the <c>magnet:?</c> prefix and
/// at least one <c>xt=urn:btih:</c> info-hash of SHA-1 (40 hex chars) or SHA-256
/// (64 hex chars) form, then wraps the input in an <see cref="AddTorrentParams"/>
/// ready for <see cref="LibtorrentSession.Add"/>.
/// </summary>
/// <remarks>
/// libtorrent handles the deeper parse (trackers, display-name, select-only filters)
/// inside the native <c>parse_magnet_uri</c> call that <see cref="LibtorrentSession.Add"/>
/// dispatches to. This helper is a cheap pre-flight so bad user input fails with a
/// typed <see cref="ArgumentException"/> at the managed boundary instead of surfacing
/// as an invalid <see cref="MagnetHandle"/> later on.
/// </remarks>
public static class MagnetUri
{
    private const string MagnetPrefix = "magnet:?";
    private const string XtBtihPrefix = "xt=urn:btih:";
    private const string DnPrefix = "dn=";

    /// <summary>
    /// Parses a magnet URI into <see cref="AddTorrentParams"/> ready for <see cref="LibtorrentSession.Add"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The URI is null, empty, lacks the <c>magnet:?</c> prefix, or has no valid <c>xt=urn:btih:</c> hash.</exception>
    public static AddTorrentParams Parse(string magnetUri)
    {
        if (!TryParse(magnetUri, out var parsed))
        {
            throw new ArgumentException(
                $"'{magnetUri}' is not a valid BEP-9 magnet URI (missing 'magnet:?' prefix or 'xt=urn:btih:' hash).",
                nameof(magnetUri));
        }
        return parsed;
    }

    /// <summary>
    /// Tries to parse a magnet URI. Returns <c>true</c> and populates <paramref name="parameters"/>
    /// on success; returns <c>false</c> and leaves <paramref name="parameters"/> at its default
    /// on any failure. Never throws.
    /// </summary>
    public static bool TryParse(string? magnetUri, out AddTorrentParams parameters)
    {
        parameters = default!;

        if (string.IsNullOrEmpty(magnetUri))
        {
            return false;
        }

        if (!magnetUri.StartsWith(MagnetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = magnetUri.AsSpan(MagnetPrefix.Length);
        while (!body.IsEmpty)
        {
            var sep = body.IndexOf('&');
            var part = sep >= 0 ? body[..sep] : body;
            body = sep >= 0 ? body[(sep + 1)..] : default;

            if (!part.StartsWith(XtBtihPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = part[XtBtihPrefix.Length..];
            if (hash.Length != Sha1Hash.HexLength && hash.Length != Sha256Hash.HexLength)
            {
                continue;
            }

            if (!IsAllHex(hash))
            {
                continue;
            }

            parameters = new AddTorrentParams { MagnetUri = magnetUri };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the v1 SHA-1 info-hash from <paramref name="magnetUri"/>'s
    /// <c>xt=urn:btih</c> parameter. Returns <c>false</c> for null/empty input,
    /// missing hash, non-hex content, or non-SHA-1-length hashes (v2-only magnets
    /// land here — addressed by the broader v2 work in Phase F).
    /// </summary>
    public static bool TryGetInfoHash(string? magnetUri, out Sha1Hash infoHash)
    {
        infoHash = default;

        if (string.IsNullOrEmpty(magnetUri) ||
            !magnetUri.StartsWith(MagnetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = magnetUri.AsSpan(MagnetPrefix.Length);
        while (!body.IsEmpty)
        {
            var sep = body.IndexOf('&');
            var part = sep >= 0 ? body[..sep] : body;
            body = sep >= 0 ? body[(sep + 1)..] : default;

            if (!part.StartsWith(XtBtihPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Sha1Hash.TryParse(part[XtBtihPrefix.Length..], out infoHash);
        }

        return false;
    }

    /// <summary>
    /// Returns the URL-decoded <c>dn=</c> display name when present; <c>null</c>
    /// otherwise (null/empty input, missing parameter, or empty value).
    /// </summary>
    public static string? TryGetDisplayName(string? magnetUri)
    {
        if (string.IsNullOrEmpty(magnetUri) ||
            !magnetUri.StartsWith(MagnetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var body = magnetUri.AsSpan(MagnetPrefix.Length);
        while (!body.IsEmpty)
        {
            var sep = body.IndexOf('&');
            var part = sep >= 0 ? body[..sep] : body;
            body = sep >= 0 ? body[(sep + 1)..] : default;

            if (!part.StartsWith(DnPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = part[DnPrefix.Length..];
            return raw.IsEmpty ? null : Uri.UnescapeDataString(raw.ToString());
        }

        return null;
    }

    private static bool IsAllHex(ReadOnlySpan<char> hex)
    {
        foreach (var c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }
}
