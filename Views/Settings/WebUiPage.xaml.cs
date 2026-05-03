using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;

namespace WinBit.Views.Settings;

public sealed partial class WebUiPage : Page
{
    private readonly ISettingsService _settings;
    private bool _loading = true;

    public WebUiPage()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            WebUiEnabledToggle.IsOn = _settings.Current.WebUi.Enabled;
            WebUiPortBox.Value = _settings.Current.WebUi.Port;
            WebUiHttpsToggle.IsOn = _settings.Current.WebUi.Https;
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await _settings.UpdateAsync(s => s.WebUi.Enabled = WebUiEnabledToggle.IsOn);
    }

    private async void OnHttpsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await _settings.UpdateAsync(s => s.WebUi.Https = WebUiHttpsToggle.IsOn);
    }

    private async void OnPortChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        if (double.IsNaN(args.NewValue)) return;
        var port = (int)args.NewValue;
        if (port < 1 || port > 65535) return;
        await _settings.UpdateAsync(s => s.WebUi.Port = port);
    }
}
