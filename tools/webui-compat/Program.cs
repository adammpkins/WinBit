using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinBit.Core.Hosting;
using WinBit.Core.Settings;

namespace WinBit.WebUiCompatHost;

// Minimal host for the qbittorrent-api compat oracle. Boots the full WinBit.Core service
// graph and forces the Web UI on at a caller-supplied port. Reads WEBUI_PORT / DATA_ROOT
// environment variables so the CI job can point at a scratch temp directory.
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("WEBUI_PORT"), out var p) ? p : 18080;
        var dataRoot = Environment.GetEnvironmentVariable("DATA_ROOT")
            ?? Path.Combine(Path.GetTempPath(), $"winbit-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWinBitCore(opts => opts.DataRoot = dataRoot);
        builder.Services.AddHostedService<SettingsPrimer>();
        builder.Services.AddSingleton(new CompatHostOptions(port, dataRoot));

        await builder.Build().RunAsync();
        return 0;
    }
}

internal sealed record CompatHostOptions(int Port, string DataRoot);

/// <summary>Runs before the Web UI service's StartAsync and forces Enabled/Port.</summary>
internal sealed class SettingsPrimer : IHostedService
{
    private readonly ISettingsService _settings;
    private readonly CompatHostOptions _options;

    public SettingsPrimer(ISettingsService settings, CompatHostOptions options)
    {
        _settings = settings;
        _options = options;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // LoadAsync hydrates whatever was persisted; we then overwrite the two fields the
        // compat harness needs deterministic.
        await _settings.LoadAsync(ct).ConfigureAwait(false);
        await _settings.UpdateAsync(s =>
        {
            s.WebUi.Enabled = true;
            s.WebUi.Port = _options.Port;
            s.Downloads.DefaultSavePath = Path.Combine(_options.DataRoot, "downloads");
            Directory.CreateDirectory(s.Downloads.DefaultSavePath);
        }, ct).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
