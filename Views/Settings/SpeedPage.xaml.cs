using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;

namespace WinBit.Views.Settings;

public sealed partial class SpeedPage : Page
{
    private const int BytesPerKilobyte = 1024;

    private readonly ISettingsService _settings;
    private bool _loading;

    public SpeedPage()
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
            var s = _settings.Current.Speed;
            GlobalDownBox.Value = BpsToKbps(s.GlobalDownBps);
            GlobalUpBox.Value = BpsToKbps(s.GlobalUpBps);
            AltDownBox.Value = BpsToKbps(s.AltDownBps);
            AltUpBox.Value = BpsToKbps(s.AltUpBps);
            AltEnabledToggle.IsOn = s.AltEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    private static double BpsToKbps(int bps) => bps <= 0 ? 0 : (double)bps / BytesPerKilobyte;

    private static int KbpsToBps(double kbps)
    {
        if (double.IsNaN(kbps) || kbps <= 0)
        {
            return 0;
        }
        var bps = kbps * BytesPerKilobyte;
        return bps >= int.MaxValue ? int.MaxValue : (int)bps;
    }

    private async void OnGlobalDownChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        var value = KbpsToBps(args.NewValue);
        await _settings.UpdateAsync(s => s.Speed.GlobalDownBps = value);
    }

    private async void OnGlobalUpChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        var value = KbpsToBps(args.NewValue);
        await _settings.UpdateAsync(s => s.Speed.GlobalUpBps = value);
    }

    private async void OnAltDownChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        var value = KbpsToBps(args.NewValue);
        await _settings.UpdateAsync(s => s.Speed.AltDownBps = value);
    }

    private async void OnAltUpChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        var value = KbpsToBps(args.NewValue);
        await _settings.UpdateAsync(s => s.Speed.AltUpBps = value);
    }

    private async void OnAltEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var enabled = AltEnabledToggle.IsOn;
        await _settings.UpdateAsync(s => s.Speed.AltEnabled = enabled);
    }
}
