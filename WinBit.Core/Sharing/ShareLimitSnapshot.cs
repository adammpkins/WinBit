using WinBit.Core.BitTorrent;
using WinBit.Core.Common;

namespace WinBit.Core.Sharing;

/// <summary>
/// Per-torrent state that the share-limit enforcement loop reads once per tick. Everything
/// the evaluator needs *except* <c>SeedingTime</c> / <c>InactiveSeedingTime</c>, which the
/// loop derives from successive snapshots.
/// </summary>
public readonly record struct ShareLimitSnapshot(
    TorrentId Id,
    TorrentState State,
    bool IsFinished,
    bool IsForced,
    bool IsStopped,
    bool IsSuperSeeding,
    double Ratio,
    long BytesUploaded);
