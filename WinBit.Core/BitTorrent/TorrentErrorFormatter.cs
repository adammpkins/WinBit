namespace WinBit.Core.BitTorrent;

/// <summary>
/// Renders MonoTorrent's <see cref="MonoTorrent.Client.Error"/> into a short human-readable string
/// suitable for a toast body or a transfer-list tooltip. The engine exposes disk read/write
/// failures plus an exception chain; the friendly prefix names the failure category and the
/// appended message carries the OS-level detail (e.g. "No space left on device").
/// </summary>
public static class TorrentErrorFormatter
{
    public static string? Format(MonoTorrent.Client.Error? error)
    {
        if (error is null)
        {
            return null;
        }
        var reason = error.Reason switch
        {
            MonoTorrent.Client.Reason.ReadFailure => "Disk read failure",
            MonoTorrent.Client.Reason.WriteFailure => "Disk write failure",
            _ => error.Reason.ToString(),
        };
        var detail = error.Exception?.Message;
        return string.IsNullOrWhiteSpace(detail) ? reason : $"{reason}: {detail}";
    }
}
