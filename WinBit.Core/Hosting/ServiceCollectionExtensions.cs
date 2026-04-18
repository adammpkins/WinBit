using Microsoft.Extensions.DependencyInjection;
using WinBit.Core.BitTorrent;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;

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
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<SqliteTorrentStateStore>();
        services.AddSingleton<ITorrentSessionService, TorrentSessionService>();

        return services;
    }
}
