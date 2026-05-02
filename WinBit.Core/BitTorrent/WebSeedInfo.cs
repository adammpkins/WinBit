namespace WinBit.Core.BitTorrent;

/// <summary>
/// A single web seed URL attached to a torrent (BEP-19 / BEP-17).
/// </summary>
public sealed record WebSeedInfo
{
    public required Uri Url { get; init; }
}
