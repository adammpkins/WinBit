using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinBit.Core.Logging;
using WinBit.Core.Notifications;

namespace WinBit.Services;

/// <summary>
/// Windows AppNotifications implementation of <see cref="INotificationService"/>. Pushes an
/// actionable toast into Action Center for each event; the <c>openFolder</c> activation opens
/// the torrent's save path in File Explorer.
/// </summary>
public sealed class AppToastNotificationService : INotificationService, IDisposable
{
    private const string ActionKey = "action";
    private const string ActionOpenFolder = "openFolder";
    private const string PathKey = "path";

    private readonly ILogService _log;
    private bool _registered;

    public AppToastNotificationService(ILogService log)
    {
        _log = log;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            _log.Write($"Toast notifications unavailable: {ex.Message}", LogSeverity.Warning);
        }
    }

    public Task NotifyTorrentCompletedAsync(string name, string savePath, CancellationToken ct = default)
    {
        if (!_registered)
        {
            return Task.CompletedTask;
        }
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText("Download complete")
                .AddText(name)
                .AddArgument(ActionKey, ActionOpenFolder);
            if (!string.IsNullOrEmpty(savePath))
            {
                builder.AddArgument(PathKey, savePath);
                builder.AddText(savePath);
            }
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            _log.Write($"Failed to show completion toast for \"{name}\": {ex.Message}", LogSeverity.Warning);
        }
        return Task.CompletedTask;
    }

    public Task NotifyTorrentErrorAsync(string name, string? errorMessage, CancellationToken ct = default)
    {
        if (!_registered)
        {
            return Task.CompletedTask;
        }
        try
        {
            var detail = string.IsNullOrWhiteSpace(errorMessage)
                ? "The torrent stopped because of an error."
                : errorMessage!;
            var toast = new AppNotificationBuilder()
                .AddText("Torrent error")
                .AddText(name)
                .AddText(detail)
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
        catch (Exception ex)
        {
            _log.Write($"Failed to show error toast for \"{name}\": {ex.Message}", LogSeverity.Warning);
        }
        return Task.CompletedTask;
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue(ActionKey, out var action))
        {
            return;
        }
        if (action == ActionOpenFolder && args.Arguments.TryGetValue(PathKey, out var path) && !string.IsNullOrEmpty(path))
        {
            OpenInExplorer(path);
        }
    }

    private void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Write($"Failed to open \"{path}\": {ex.Message}", LogSeverity.Warning);
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
            // Unregister can throw during process teardown; swallow.
        }
        _registered = false;
    }
}
