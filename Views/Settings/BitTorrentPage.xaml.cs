using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;
using WinBit.Views.Dialogs;

namespace WinBit.Views.Settings;

public sealed partial class BitTorrentPage : Page
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public BitTorrentPage()
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
            var b = _settings.Current.BitTorrent;
            DhtToggle.IsOn = b.Dht;
            PexToggle.IsOn = b.Pex;
            LsdToggle.IsOn = b.Lsd;
            EncryptionCombo.SelectedIndex = b.Encryption switch
            {
                EncryptionMode.Require => 1,
                EncryptionMode.Disable => 2,
                _ => 0,
            };
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnEncryptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var mode = EncryptionCombo.SelectedIndex switch
        {
            1 => EncryptionMode.Require,
            2 => EncryptionMode.Disable,
            _ => EncryptionMode.Prefer,
        };
        await _settings.UpdateAsync(s => s.BitTorrent.Encryption = mode);
    }

    private async void OnDhtToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = DhtToggle.IsOn;
        await _settings.UpdateAsync(s => s.BitTorrent.Dht = value);
    }

    private async void OnPexToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = PexToggle.IsOn;
        await _settings.UpdateAsync(s => s.BitTorrent.Pex = value);
    }

    private async void OnLsdToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = LsdToggle.IsOn;
        await _settings.UpdateAsync(s => s.BitTorrent.Lsd = value);
    }

    private async void OnConfigureShareLimitsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ShareLimitsDialog(App.Services.GetRequiredService<ISettingsService>())
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
