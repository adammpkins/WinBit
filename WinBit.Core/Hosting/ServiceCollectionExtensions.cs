using Microsoft.Extensions.DependencyInjection;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Logging;
using WinBit.Core.Networking;
using WinBit.Core.Notifications;
using WinBit.Core.Persistence;
using WinBit.Core.Power;
using WinBit.Core.Rss;
using WinBit.Core.Search;
using WinBit.Core.Settings;
using WinBit.Core.Shell;
using WinBit.Core.Updates;
using WinBit.Core.Sharing;
using WinBit.Core.Stats;
using WinBit.Core.Tags;
using WinBit.Core.WatchedFolders;
using WinBit.Core.WebUi;

namespace WinBit.Core.Hosting;

/// <summary>
/// Entry point for DI composition. Registers every WinBit.Core service + hosted service.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWinBitCore(this IServiceCollection services, Action<WinBitCoreOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<WinBitCoreOptions>();
        }

        services.AddSingleton<Paths>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IPeerLogService, PeerLogService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<SqliteTorrentStateStore>();
        services.AddSingleton<ITorrentStateStore>(sp => sp.GetRequiredService<SqliteTorrentStateStore>());
        services.AddSingleton<ICategoryService, CategoryService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<IShareLimitOverrideService, ShareLimitOverrideService>();
        services.AddSingleton<IAllTimeStatsService, AllTimeStatsService>();
        services.AddHostedService<ShareLimitEnforcementLoop>();
        services.AddSingleton<IHttpClientProvider, HttpClientProvider>();
        services.AddSingleton<UrlDownloader>();
        services.AddSingleton<IIpFilterService, IpFilterService>();
        services.AddSingleton<ITorrentSessionService, TorrentSessionService>();
        services.AddSingleton<ITorrentCreatorService, TorrentCreatorService>();
        services.AddSingleton<ITorrentCreatorQueue, TorrentCreatorQueue>();
        services.AddSingleton<IRssService, RssService>();
        services.AddSingleton<RssRefreshLoop>();
        services.AddHostedService(sp => sp.GetRequiredService<RssRefreshLoop>());
        services.AddSingleton<IRssRefresher>(sp => sp.GetRequiredService<RssRefreshLoop>());
        services.AddSingleton<IRssReadStore, SqliteRssReadStore>();
        services.AddSingleton<RssArticleCache>();
        services.AddSingleton<IRssArticleCache>(sp => sp.GetRequiredService<RssArticleCache>());
        services.AddHostedService<RssReadStateHydrator>();
        services.AddSingleton<IAutoDownloaderService, AutoDownloaderService>();
        services.AddHostedService<AutoDownloaderDispatcher>();
        services.AddSingleton<IWebUiAuthService, WebUiAuthService>();
        services.AddSingleton<WebUiService>();
        services.AddSingleton<IWebUiService>(sp => sp.GetRequiredService<WebUiService>());
        services.AddHostedService(sp => sp.GetRequiredService<WebUiService>());
        services.AddHostedService<IpFilterLoader>();
        services.AddHostedService<WinBitHostedService>();
        services.AddHostedService<SpeedProfileApplier>();
        services.AddHostedService<BandwidthScheduler>();
        services.AddSingleton<PortForwardingService>();
        services.AddSingleton<IPortForwardingService>(sp => sp.GetRequiredService<PortForwardingService>());
        services.AddHostedService(sp => sp.GetRequiredService<PortForwardingService>());
        services.AddHostedService<EncryptionApplier>();
        services.AddHostedService<PeerDiscoveryApplier>();
        services.AddSingleton<WatchedFolderService>();
        services.AddSingleton<IWatchedFolderService>(sp => sp.GetRequiredService<WatchedFolderService>());
        services.AddHostedService(sp => sp.GetRequiredService<WatchedFolderService>());
        services.AddHostedService<StatusPollingLoop>();
        // Default is a no-op; the WinBit app replaces this with a Windows-AppNotifications-backed
        // implementation in Bootstrap.AddWinBitApp. Last registration wins for direct resolution.
        services.AddSingleton<INotificationService, NullNotificationService>();
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<TorrentCompletionNotifier>();
        services.AddHostedService<TorrentErrorNotifier>();
        services.AddHostedService<SlowDownloadNotifier>();
        services.AddSingleton<ISleepInhibitor, Win32SleepInhibitor>();
        services.AddHostedService<PowerManagementService>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAssociationRegistryWriter, Win32AssociationRegistryWriter>();
            services.AddSingleton<IShellAssociationService>(sp =>
            {
                var writer = sp.GetRequiredService<IAssociationRegistryWriter>();
                var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("Cannot determine current executable path.");
                return new ShellAssociationService(writer, exe);
            });
        }

        services.AddSingleton<ISearchPluginHost>(sp =>
            new SearchPluginHost(sp.GetServices<ISearchPlugin>(), sp.GetRequiredService<ILogService>()));
        services.AddHostedService<WinBit.Core.Search.Torznab.TorznabPluginRegistrar>();

        services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();

        return services;
    }
}
