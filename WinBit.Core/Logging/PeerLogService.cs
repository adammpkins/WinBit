using System.Collections.Concurrent;

namespace WinBit.Core.Logging;

public sealed class PeerLogService : IPeerLogService
{
    public const int Capacity = 5_000;

    private readonly ConcurrentQueue<PeerLogEntry> _entries = new();
    private long _nextId;

    public event EventHandler<PeerLogEntry>? EntryAdded;

    public IReadOnlyList<PeerLogEntry> Recent => _entries.ToArray();

    public void Record(string peerEndpoint, string reason)
    {
        var entry = new PeerLogEntry(
            Interlocked.Increment(ref _nextId),
            DateTime.UtcNow,
            peerEndpoint,
            reason);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
        EntryAdded?.Invoke(this, entry);
    }
}
