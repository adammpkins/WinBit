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
    public BehaviorSettings Behavior { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
}

public sealed class SearchSettings
{
    /// <summary>User-configured Torznab/Jackett endpoints. One <see cref="Search.Torznab.TorznabSearchPlugin"/>
    /// is registered per enabled entry at startup.</summary>
    public List<Search.Torznab.TorznabFeedDefinition> TorznabFeeds { get; set; } = new();
}

public sealed class BehaviorSettings
{
    /// <summary>When true, closing the main window hides it to the system tray instead of exiting.
    /// Off by default so first-run users still see the app quit on close.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>When true, a toast fires once per long-running torrent whose download rate drops
    /// below <see cref="SlowDownloadWarningRateBps"/> after
    /// <see cref="SlowDownloadWarningAfterMinutes"/> of being in the Downloading state.</summary>
    public bool SlowDownloadWarningEnabled { get; set; }

    /// <summary>Minutes a torrent must spend in the Downloading state before a low-rate
    /// warning can fire. Default: 24h.</summary>
    public int SlowDownloadWarningAfterMinutes { get; set; } = 24 * 60;

    /// <summary>Bytes/sec threshold below which the warning fires (when the other gates pass).
    /// Default: 10 KB/s.</summary>
    public long SlowDownloadWarningRateBps { get; set; } = 10 * 1024;

    /// <summary>When true, the system is kept awake while at least one torrent is actively
    /// transferring bytes. Displays still sleep on their own schedule. Default: on.</summary>
    public bool PreventSleepWhileActive { get; set; } = true;

    /// <summary>Set to true after the user has seen or dismissed the "make WinBit the default
    /// handler" prompt at least once. Reset manually if we want to nag again.</summary>
    public bool DefaultClientPromptDismissed { get; set; }

    /// <summary>Set to true after the first-run wizard has completed or been skipped. When
    /// false on startup, the wizard runs in place of the default-client prompt so the user
    /// isn't asked the same question twice.</summary>
    public bool FirstRunComplete { get; set; }
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

    /// <summary>When true + <see cref="IpFilterPath"/> points at an existing file, the engine
    /// refuses connections from any address listed in the PeerGuardian <c>.p2p</c> blocklist.</summary>
    public bool IpFilterEnabled { get; set; }

    public string? IpFilterPath { get; set; }

    public ProxyType ProxyType { get; set; } = ProxyType.None;

    public string? ProxyHost { get; set; }

    public int ProxyPort { get; set; } = 1080;

    public string? ProxyUsername { get; set; }

    public string? ProxyPassword { get; set; }
}

public enum ProxyType
{
    None,
    Http,
    Socks5,
}

public sealed class SpeedSettings
{
    public int GlobalDownBps { get; set; }
    public int GlobalUpBps { get; set; }
    public int AltDownBps { get; set; }
    public int AltUpBps { get; set; }
    public bool AltEnabled { get; set; }

    public bool SchedulerEnabled { get; set; }
    public TimeOnly SchedulerStartTime { get; set; } = new(8, 0);
    public TimeOnly SchedulerEndTime { get; set; } = new(20, 0);
    public BandwidthScheduleDays SchedulerDays { get; set; } = BandwidthScheduleDays.EveryDay;
}

/// <summary>
/// Ported from qBittorrent's <c>Scheduler::Days</c> enum (see
/// <c>qbittorrent/src/base/preferences.h</c>). Ordering preserved for JSON stability.
/// </summary>
public enum BandwidthScheduleDays
{
    EveryDay,
    Weekday,
    Weekend,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday,
}

public sealed class BitTorrentSettings
{
    public bool Dht { get; set; } = true;
    public bool Pex { get; set; } = true;
    public bool Lsd { get; set; } = true;
    public EncryptionMode Encryption { get; set; } = EncryptionMode.Prefer;

    public Sharing.ShareLimits GlobalShareLimits { get; set; } = new();
}

/// <summary>
/// Message Stream Encryption preference mirroring qBittorrent's three-way combo. Maps onto
/// MonoTorrent's <c>EngineSettings.AllowedEncryption</c>: Prefer = all three, Require = RC4
/// variants only (no plain-text), Disable = plain-text only.
/// </summary>
public enum EncryptionMode
{
    Prefer,
    Require,
    Disable,
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

    /// <summary>Path to a user-supplied PFX certificate. When null/empty and <see cref="Https"/>
    /// is true, WinBit generates a self-signed cert at <c>%LOCALAPPDATA%\WinBit\webui-self-signed.pfx</c>
    /// and reuses it on subsequent starts.</summary>
    public string? HttpsCertPath { get; set; }

    /// <summary>Password for the user-supplied PFX. Ignored when the cert is generated here.</summary>
    public string? HttpsCertPassword { get; set; }

    /// <summary>Username for Web UI login. Defaults to <c>admin</c> when null/empty.</summary>
    public string? Username { get; set; }

    /// <summary>PBKDF2 hash in <c>base64(salt):base64(hash)</c> form. When null/empty the Web
    /// UI accepts the documented default password (<c>adminadmin</c>) so first-run users are
    /// not locked out.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>CIDR subnets whose clients bypass the login check (e.g. <c>192.168.1.0/24</c>).
    /// Empty = every request needs a valid SID cookie.</summary>
    public List<string> WhitelistedSubnets { get; set; } = new();

    /// <summary>When true, the root URL serves WinBit's Fluent-flavored native web client
    /// instead of qBittorrent's HTML admin UI. The native client is always reachable at
    /// <c>/winbit/</c>; qBittorrent's UI stays reachable at <c>/qbittorrent/</c>.</summary>
    public bool UseNativeClient { get; set; }
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

    /// <summary>IETF BCP-47 tag (e.g. "en-US", "fr-FR") or null/empty for the OS default.
    /// Applied once at startup via <c>ApplicationLanguages.PrimaryLanguageOverride</c>; changing
    /// it requires an app restart for the new strings to take effect.</summary>
    public string? LanguageTag { get; set; }

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
