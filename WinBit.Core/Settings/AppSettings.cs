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
}
