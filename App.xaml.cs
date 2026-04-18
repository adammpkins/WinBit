using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Hosting;
using WinBit.Core.Settings;
using WinBit.Core.Shell;
using WinBit.Core.Sharing;
using WinBit.Core.Tags;
using WinBit.Infrastructure;
using WinBit.Services;
using WinBit.Views.Dialogs;

namespace WinBit;

public partial class App : Application
{
    private const string SingleInstanceName = "WinBit.SingleInstance.v1";

    private IHost? _host;
    private Window? _window;
    private NamedPipeSingleInstance? _singleInstance;
    private DispatcherQueue? _dispatcher;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host has not been built yet.");

    public static Window? MainWindow => ((App)Current)._window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _singleInstance = new NamedPipeSingleInstance(SingleInstanceName);
        _singleInstance.TryAcquirePrimary();
        if (!_singleInstance.IsPrimary)
        {
            // Hand our command line to the running primary and exit silently.
            await _singleInstance.ForwardAsync(Environment.CommandLine, TimeSpan.FromSeconds(3));
            _singleInstance.Dispose();
            _singleInstance = null;
            Current.Exit();
            return;
        }

        var dispatcher = _dispatcher;
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
        ApplyLanguageOverride(_host.Services.GetRequiredService<ISettingsService>().Current.UiState.LanguageTag);
        AccentService.Apply(_host.Services.GetRequiredService<ISettingsService>().Current.UiState.AccentColor);
        await _host.Services.GetRequiredService<IThemeService>().InitializeAsync();

        _window = new MainWindow();
        _window.Closed += OnWindowClosed;
        _window.Activate();

        _ = HandleActivationAsync(_window, ActivationArguments.Parse(Environment.GetCommandLineArgs()[1..]));
        _ = MaybeShowFirstRunAsync(_window);

        _singleInstance.StartListening(OnForwardedActivation);
    }

    private static async Task MaybeShowFirstRunAsync(Window window)
    {
        var settings = Services.GetRequiredService<ISettingsService>();
        if (settings.Current.Behavior.FirstRunComplete)
        {
            await MaybePromptDefaultClientAsync(window);
            return;
        }

        await Task.Yield();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var dialog = new FirstRunWizard(
            settings,
            Services.GetService<IShellAssociationService>(),
            hwnd)
        {
            XamlRoot = window.Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void OnForwardedActivation(string commandLine)
    {
        var activation = ActivationArguments.ParseCommandLine(commandLine);
        if (!activation.HasWork || _window is null || _dispatcher is null)
        {
            return;
        }
        var window = _window;
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                window.AppWindow.Show();
                window.Activate();
                await HandleActivationAsync(window, activation);
            }
            catch
            {
                // Forwarded activation failures shouldn't crash the primary.
            }
        });
    }

    private static async Task MaybePromptDefaultClientAsync(Window window)
    {
        var associations = Services.GetService<IShellAssociationService>();
        if (associations is null)
        {
            return;
        }

        var settings = Services.GetRequiredService<ISettingsService>();
        var status = associations.GetStatus();
        if (!DefaultClientPromptPolicy.ShouldPrompt(status, settings.Current.Behavior))
        {
            return;
        }

        // Defer a tick so the window is fully rendered before the dialog lands.
        await Task.Yield();
        var dialog = new DefaultClientDialog(associations, settings, status)
        {
            XamlRoot = window.Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private static async Task HandleActivationAsync(Window window, ActivationArguments activation)
    {
        if (!activation.HasWork)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        if (activation.MagnetUri is { } magnet)
        {
            var dialog = new AddMagnetDialog(
                Services.GetRequiredService<ITorrentSessionService>(),
                Services.GetRequiredService<ICategoryService>(),
                Services.GetRequiredService<ITagService>(),
                Services.GetRequiredService<IShareLimitOverrideService>(),
                Services.GetRequiredService<ISettingsService>(),
                hwnd)
            {
                XamlRoot = window.Content.XamlRoot,
            };
            dialog.SetMagnet(magnet);
            await dialog.ShowAsync();
            return;
        }

        if (activation.TorrentFilePath is { } path)
        {
            var dialog = new AddTorrentDialog(
                Services.GetRequiredService<ITorrentSessionService>(),
                Services.GetRequiredService<ISettingsService>(),
                Services.GetRequiredService<ICategoryService>(),
                Services.GetRequiredService<ITagService>(),
                Services.GetRequiredService<IShareLimitOverrideService>(),
                hwnd)
            {
                XamlRoot = window.Content.XamlRoot,
            };
            await dialog.PreloadTorrentAsync(path);
            await dialog.ShowAsync();
        }
    }

    private static void ApplyLanguageOverride(string? tag)
    {
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag ?? string.Empty;
        }
        catch
        {
            // Invalid tag or unsupported platform — fall back to the OS default.
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;

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
