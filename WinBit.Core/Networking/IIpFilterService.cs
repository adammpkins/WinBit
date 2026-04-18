using System.Net;

namespace WinBit.Core.Networking;

/// <summary>
/// In-memory peer blocklist — usually populated from a PeerGuardian <c>.p2p</c> file.
/// <c>TorrentSessionService</c> consults <see cref="IsBlocked"/> on every
/// <c>ConnectionManager.BanPeer</c> attempt, so the implementation must be O(log n) or better.
/// </summary>
public interface IIpFilterService
{
    int RuleCount { get; }

    /// <summary>Replaces the in-memory ruleset with the entries parsed from <paramref name="path"/>.</summary>
    Task LoadAsync(string path, CancellationToken ct = default);

    /// <summary>Drops every rule. Subsequent <see cref="IsBlocked"/> calls return <c>false</c>.</summary>
    void Clear();

    bool IsBlocked(IPAddress address);
}
