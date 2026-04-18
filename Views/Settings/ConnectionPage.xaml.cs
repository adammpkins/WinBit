using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;

namespace WinBit.Views.Settings;

public sealed partial class ConnectionPage : Page
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public ConnectionPage()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            var c = _settings.Current.Connection;
            ListenPortBox.Value = c.ListenPort;
            UpnpToggle.IsOn = c.Upnp;
            ProxyTypeCombo.SelectedIndex = c.ProxyType switch
            {
                ProxyType.Http => 1,
                ProxyType.Socks5 => 2,
                _ => 0,
            };
            ProxyHostBox.Text = c.ProxyHost ?? string.Empty;
            ProxyPortBox.Value = c.ProxyPort;
            ProxyUsernameBox.Text = c.ProxyUsername ?? string.Empty;
            ProxyPasswordBox.Password = c.ProxyPassword ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnListenPortChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }
        var value = (int)args.NewValue;
        await _settings.UpdateAsync(s => s.Connection.ListenPort = value);
    }

    private async void OnUpnpToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var enabled = UpnpToggle.IsOn;
        await _settings.UpdateAsync(s => s.Connection.Upnp = enabled);
    }

    private async void OnProxyTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var type = ProxyTypeCombo.SelectedIndex switch
        {
            1 => ProxyType.Http,
            2 => ProxyType.Socks5,
            _ => ProxyType.None,
        };
        await _settings.UpdateAsync(s => s.Connection.ProxyType = type);
    }

    private async void OnProxyHostChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var host = ProxyHostBox.Text;
        await _settings.UpdateAsync(s => s.Connection.ProxyHost = string.IsNullOrWhiteSpace(host) ? null : host);
    }

    private async void OnProxyPortChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }
        var port = (int)args.NewValue;
        await _settings.UpdateAsync(s => s.Connection.ProxyPort = port);
    }

    private async void OnProxyUsernameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var user = ProxyUsernameBox.Text;
        await _settings.UpdateAsync(s => s.Connection.ProxyUsername = string.IsNullOrEmpty(user) ? null : user);
    }

    private async void OnProxyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var pass = ProxyPasswordBox.Password;
        await _settings.UpdateAsync(s => s.Connection.ProxyPassword = string.IsNullOrEmpty(pass) ? null : pass);
    }
}
