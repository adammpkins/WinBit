namespace WinBit.Core.BitTorrent;

/// <summary>
/// Session-level stats rendered in the shell status bar. Captured once per <c>StatusPollingLoop</c>
/// tick off the MonoTorrent <c>ClientEngine</c>: global rates, open connections, DHT node count.
/// Zero on every axis when the engine hasn't started yet.
/// </summary>
public readonly record struct SessionStats(
    long GlobalDownloadBps,
    long GlobalUploadBps,
    int OpenConnections,
    int DhtNodes,
    long SessionDownloadedBytes = 0,
    long SessionUploadedBytes = 0);
