#nullable enable
using System;

namespace LibtorrentSharp;

/// <summary>
/// Declarative inputs for <see cref="LibtorrentSession.Add"/>. Mirrors a subset of
/// libtorrent's <c>add_torrent_params</c>: pass exactly one of <see cref="MagnetUri"/>,
/// <see cref="TorrentInfo"/>, or <see cref="ResumeData"/> to describe the torrent, plus
/// an optional <see cref="SavePath"/> override.
/// </summary>
/// <remarks>
/// Additional <c>add_torrent_params</c> fields not yet exposed will be added in future releases.
/// </remarks>
public sealed record AddTorrentParams
{
    /// <summary>BEP-9 magnet URI. Mutually exclusive with <see cref="TorrentInfo"/> and <see cref="ResumeData"/>.</summary>
    public string? MagnetUri { get; init; }

    /// <summary>Parsed <c>.torrent</c> metadata. Mutually exclusive with <see cref="MagnetUri"/> and <see cref="ResumeData"/>.</summary>
    public TorrentInfo? TorrentInfo { get; init; }

    /// <summary>Previously-captured resume blob. Mutually exclusive with <see cref="MagnetUri"/> and <see cref="TorrentInfo"/>.</summary>
    public byte[]? ResumeData { get; init; }

    /// <summary>Destination directory. Falls back to <see cref="LibtorrentSession.DefaultDownloadPath"/> when null.</summary>
    public string? SavePath { get; init; }

    internal AddTorrentSource ResolveSource()
    {
        var sources = (MagnetUri is not null ? 1 : 0)
            + (TorrentInfo is not null ? 1 : 0)
            + (ResumeData is not null ? 1 : 0);

        if (sources == 0)
        {
            throw new ArgumentException(
                "AddTorrentParams requires exactly one of MagnetUri, TorrentInfo, or ResumeData to be set.");
        }

        if (sources > 1)
        {
            throw new ArgumentException(
                "AddTorrentParams forbids setting more than one of MagnetUri, TorrentInfo, or ResumeData simultaneously.");
        }

        if (MagnetUri is not null)
        {
            return AddTorrentSource.Magnet;
        }
        if (TorrentInfo is not null)
        {
            return AddTorrentSource.TorrentInfo;
        }
        return AddTorrentSource.Resume;
    }
}

internal enum AddTorrentSource
{
    Magnet,
    TorrentInfo,
    Resume,
}

/// <summary>
/// Outcome of <see cref="LibtorrentSession.Add"/>. Exactly one of <see cref="Torrent"/>
/// and <see cref="Magnet"/> is populated on a successful add; an invalid <see cref="Magnet"/>
/// surfaces when libtorrent rejects a malformed magnet URI or resume blob (matching
/// the historical per-source behavior). File-based adds throw instead of returning
/// an invalid handle.
/// </summary>
public readonly record struct AddTorrentResult
{
    /// <summary>Populated when the source was <see cref="AddTorrentParams.TorrentInfo"/>.</summary>
    public TorrentHandle? Torrent { get; }

    /// <summary>Populated when the source was <see cref="AddTorrentParams.MagnetUri"/> or <see cref="AddTorrentParams.ResumeData"/>.</summary>
    public MagnetHandle? Magnet { get; }

    /// <summary>
    /// True when the add produced a usable handle. For magnet or resume sources this also
    /// requires <see cref="MagnetHandle.IsValid"/>, which stays false for malformed inputs.
    /// </summary>
    public bool IsValid => Torrent is not null || Magnet is { IsValid: true };

    internal AddTorrentResult(TorrentHandle torrent)
    {
        Torrent = torrent;
        Magnet = null;
    }

    internal AddTorrentResult(MagnetHandle magnet)
    {
        Torrent = null;
        Magnet = magnet;
    }
}
