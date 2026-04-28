using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinBit.Core.BitTorrent;
using WinBit.Core.Categories;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
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
    private DispatcherQueue? _dispatcher;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host has not been built yet.");

    public static Window? MainWindow => ((App)Current)._window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnAppUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        TryLog($"UI unhandled: {e.Message}\n{e.Exception}");
        e.Handled = false;
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        TryLog($"Domain unhandled: {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLog($"Unobserved task: {e.Exception}");
        e.SetObserved();
    }

    private static void TryLog(string message)
    {
        try
        {
            ((App)Current)._host?.Services.GetService<ILogService>()
                ?.Write(message, LogSeverity.Critical);
        }
        catch
        {
            // Logging is best-effort.
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // WinAppSDK's AppInstance gives us the real activation payload (file/protocol/launch)
        // and handles single-instance forwarding for both packaged and unpackaged runs. The
        // older NamedPipeSingleInstance path can't see manifest-routed file activations
        // because MSIX doesn't stuff the file path into Environment.CommandLine.
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var primary = AppInstance.FindOrRegisterForKey(SingleInstanceName);
        if (!primary.IsCurrent)
        {
            await primary.RedirectActivationToAsync(activatedArgs);
            Current.Exit();
            return;
        }

        AppInstance.GetCurrent().Activated += OnAppInstanceActivated;

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

        // Settings must load before hosted services start — WebUiService, EncryptionApplier,
        // and peers read AppSettings in StartAsync and pin to whatever they see on the first
        // read. Without this ordering, enabling WebUI (or tweaking encryption, etc.) requires
        // a restart because the services silently latched the defaults.
        await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();
        await _host.StartAsync();

        ApplyLanguageOverride(_host.Services.GetRequiredService<ISettingsService>().Current.UiState.LanguageTag);
        AccentService.Apply(_host.Services.GetRequiredService<ISettingsService>().Current.UiState.AccentColor);
        await _host.Services.GetRequiredService<IThemeService>().InitializeAsync();

        _window = new MainWindow();
        _window.Closed += OnWindowClosed;
        _window.Activate();

        var activation = ConvertActivation(activatedArgs);
        _ = RunBackgroundAsync(HandleActivationAsync(_window, activation), "HandleActivation");
        _ = RunBackgroundAsync(MaybeShowFirstRunAsync(_window), "MaybeShowFirstRun");
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        var activation = ConvertActivation(e);
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
            catch (Exception ex)
            {
                TryLog($"Redirected activation failed: {ex}");
            }
        });
    }

    private static ActivationArguments ConvertActivation(AppActivationArguments args)
    {
        return args.Kind switch
        {
            ExtendedActivationKind.File => FromFileActivation(args.Data),
            ExtendedActivationKind.Protocol => FromProtocolActivation(args.Data),
            ExtendedActivationKind.Launch => FromLaunchActivation(args.Data),
            _ => ActivationArguments.None,
        };
    }

    private static ActivationArguments FromFileActivation(object? data)
    {
        if (data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs files)
        {
            foreach (var item in files.Files)
            {
                if (item is Windows.Storage.StorageFile file
                    && string.Equals(file.FileType, ".torrent", StringComparison.OrdinalIgnoreCase))
                {
                    return new ActivationArguments(file.Path, null);
                }
            }
        }
        return ActivationArguments.None;
    }

    private static ActivationArguments FromProtocolActivation(object? data)
    {
        if (data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs proto
            && proto.Uri is { } uri
            && string.Equals(uri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            return new ActivationArguments(null, uri.ToString());
        }
        return ActivationArguments.None;
    }

    private static ActivationArguments FromLaunchActivation(object? data)
    {
        if (data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
        {
            return ActivationArguments.ParseCommandLine(launch.Arguments);
        }
        return ActivationArguments.None;
    }

    private static async Task MaybeShowFirstRunAsync(Window window)
    {
        var settings = Services.GetRequiredService<ISettingsService>();
        if (settings.Current.Behavior.FirstRunComplete)
        {
            await MaybePromptDefaultClientAsync(window);
            return;
        }

        await WaitForXamlRootAsync(window);
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

    private static async Task RunBackgroundAsync(Task work, string label)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryLog($"Background task '{label}' failed: {ex}");
        }
    }

    private static Task WaitForXamlRootAsync(Window window)
    {
        if (window.Content is FrameworkElement ready && ready.XamlRoot is not null && ready.IsLoaded)
        {
            return Task.CompletedTask;
        }
        if (window.Content is not FrameworkElement element)
        {
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource();
        void Handler(object s, RoutedEventArgs e)
        {
            element.Loaded -= Handler;
            tcs.TrySetResult();
        }
        element.Loaded += Handler;
        return tcs.Task;
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

        await WaitForXamlRootAsync(window);
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
