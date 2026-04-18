namespace WinBit.Core.Logging;

public sealed record PeerLogEntry(long Id, DateTime TimestampUtc, string PeerEndpoint, string Reason);
