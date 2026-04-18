namespace WinBit.Core.Settings;

/// <summary>
/// Top-level settings POCO. Section classes are filled in across M2..M12.
/// </summary>
public sealed class AppSettings
{
    public DownloadsSettings Downloads { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();
    public SpeedSettings Speed { get; set; } = new();
    public BitTorrentSettings BitTorrent { get; set; } = new();
    public RssSettings Rss { get; set; } = new();
    public WebUiSettings WebUi { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();
    public UiStateSettings UiState { get; set; } = new();
}

public sealed class DownloadsSettings
{
    public string? DefaultSavePath { get; set; }
    public bool AutoTmmEnabled { get; set; }
    public bool PreAllocate { get; set; }
}

public sealed class ConnectionSettings
{
    public int ListenPort { get; set; } = 6881;
    public bool Upnp { get; set; } = true;
}

public sealed class SpeedSettings
{
    public int GlobalDownBps { get; set; }
    public int GlobalUpBps { get; set; }
    public int AltDownBps { get; set; }
    public int AltUpBps { get; set; }
    public bool AltEnabled { get; set; }
}

public sealed class BitTorrentSettings
{
    public bool Dht { get; set; } = true;
    public bool Pex { get; set; } = true;
    public bool Lsd { get; set; } = true;
    public string Encryption { get; set; } = "Prefer";

    public Sharing.ShareLimits GlobalShareLimits { get; set; } = new();
}

public sealed class RssSettings
{
    public bool Enabled { get; set; } = true;
    public int RefreshIntervalMinutes { get; set; } = 30;
    public int MaxArticlesPerFeed { get; set; } = 100;
    public bool AutoDownloader { get; set; }
}

public sealed class WebUiSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 8080;
    public bool Https { get; set; }
}

public sealed class AdvancedSettings
{
    public int AsyncIoThreads { get; set; } = 4;
}

public sealed class UiStateSettings
{
    /// <summary>"Light" | "Dark" | "System"</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Accent hex (e.g. "#0078D4") or null for system accent.</summary>
    public string? AccentColor { get; set; }

    public int SidebarWidth { get; set; } = 240;

    public TransfersGridLayout TransfersGrid { get; set; } = new();

    /// <summary>Most-recently-used save paths from add dialogs. First entry = most recent.</summary>
    public List<string> RecentSavePaths { get; set; } = new();
}

public static class RecentPathsHelper
{
    public const int DefaultCap = 8;

    /// <summary>MRU push: dedupes case-insensitively, prepends, and trims to <paramref name="cap"/>.</summary>
    public static void PushMru(List<string> list, string path, int cap = DefaultCap)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        while (list.Count > cap)
        {
            list.RemoveAt(list.Count - 1);
        }
    }
}

/// <summary>
/// Per-user transfer-grid layout. Keys are stable column tags ("name", "size", etc.); entries
/// capture pixel width, horizontal order, and current sort direction. A null <see cref="Columns"/>
/// dictionary or a missing entry means "use the column's designed default".
/// </summary>
public sealed class TransfersGridLayout
{
    public Dictionary<string, TransferColumnState> Columns { get; set; } = new();
}

public sealed class TransferColumnState
{
    public double Width { get; set; }
    public int Order { get; set; }
    /// <summary>null = unsorted, "Ascending", or "Descending".</summary>
    public string? SortDirection { get; set; }
}
