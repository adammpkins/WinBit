using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinBit.Core.Hosting;
using WinBit.Core.Settings;
using WinBit.Infrastructure;
using WinBit.Services;

namespace WinBit;

public partial class App : Application
{
    private IHost? _host;
    private Window? _window;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host has not been built yet.");

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();

        _host = Host.CreateApplicationBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dispatcher);
                services.AddSingleton<IDispatcherQueueProvider>(_ => new DispatcherQueueProvider(dispatcher));
                services.AddWinBitCore();
                services.AddWinBitApp();
            })
            .Build();

        _host.Start();

        await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();
        await _host.Services.GetRequiredService<IThemeService>().InitializeAsync();

        _window = new MainWindow();
        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }
}

internal static class HostBuilderExtensions
{
    public static HostApplicationBuilder ConfigureServices(this HostApplicationBuilder builder, Action<IServiceCollection> configure)
    {
        configure(builder.Services);
        return builder;
    }
}
