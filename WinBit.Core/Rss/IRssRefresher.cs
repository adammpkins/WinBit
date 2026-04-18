namespace WinBit.Core.Rss;

/// <summary>
/// On-demand feed refresh primitive. Used by the Web UI's <c>refreshItem</c> endpoint (and
/// any future "refresh now" UI action) to bypass the refresh-loop interval gate for a
/// specific feed URL.
/// </summary>
public interface IRssRefresher
{
    Task RefreshFeedAsync(string feedUrl, CancellationToken ct = default);
}
