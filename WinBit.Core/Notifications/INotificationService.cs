namespace WinBit.Core.Notifications;

/// <summary>
/// Surfaces desktop notifications for user-visible events that happen outside the main window.
/// Core defines the contract; the WinUI app provides an implementation backed by
/// Microsoft.Windows.AppNotifications. Headless hosts (compat host, tests) fall back to
/// <see cref="NullNotificationService"/>.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shown when a torrent transitions from not-complete to 100% within the running session.
    /// Implementations typically present a toast whose primary activation opens
    /// <paramref name="savePath"/> in Explorer.
    /// </summary>
    Task NotifyTorrentCompletedAsync(string name, string savePath, CancellationToken ct = default);

    /// <summary>
    /// Shown when a torrent enters <c>TorrentState.Error</c> after previously being in a healthy
    /// state. <paramref name="errorMessage"/> is a short human-readable summary (e.g. the tracker
    /// failure message); pass <c>null</c> when no detail is available and a generic string will
    /// be rendered.
    /// </summary>
    Task NotifyTorrentErrorAsync(string name, string? errorMessage, CancellationToken ct = default);
}

/// <summary>No-op fallback registered when the app-level toast implementation isn't in play.</summary>
public sealed class NullNotificationService : INotificationService
{
    public Task NotifyTorrentCompletedAsync(string name, string savePath, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyTorrentErrorAsync(string name, string? errorMessage, CancellationToken ct = default) =>
        Task.CompletedTask;
}
