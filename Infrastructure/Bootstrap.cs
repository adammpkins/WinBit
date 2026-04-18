using Microsoft.Extensions.DependencyInjection;
using WinBit.Services;
using WinBit.ViewModels.Logs;
using WinBit.ViewModels.Shell;
using WinBit.ViewModels.Stats;
using WinBit.ViewModels.Transfers;
using WinBit.Views.Logs;
using WinBit.Views.Rss;
using WinBit.Views.Settings;
using WinBit.Views.Shell;
using WinBit.Views.Stats;
using WinBit.Views.Transfers;

namespace WinBit.Infrastructure;

public static class Bootstrap
{
    public static IServiceCollection AddWinBitApp(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        services.AddTransient<ShellViewModel>();
        services.AddSingleton<ShellStatusViewModel>();
        services.AddTransient<TransfersViewModel>();
        services.AddSingleton<StatsViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<PeerLogViewModel>();

        services.AddTransient<ShellPage>();
        services.AddTransient<TransfersPage>();
        services.AddTransient<StatsPage>();
        services.AddTransient<LogsPage>();
        services.AddTransient<ComingSoonPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<DownloadsPage>();
        services.AddTransient<ConnectionPage>();
        services.AddTransient<SpeedPage>();
        services.AddTransient<BitTorrentPage>();
        services.AddTransient<RssPage>();
        services.AddTransient<RssReaderPage>();
        services.AddTransient<AutoDownloaderPage>();
        services.AddTransient<WebUiPage>();
        services.AddTransient<AdvancedPage>();

        return services;
    }
}
