using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;

namespace WinBit.Views.Settings;

public sealed partial class SpeedPage : Page
{
    private const int BytesPerKilobyte = 1024;

    private readonly ISettingsService _settings;
    private bool _loading = true;

    public SpeedPage()
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
            var s = _settings.Current.Speed;
            GlobalDownBox.Value = BpsToKbps(s.GlobalDownBps);
            GlobalUpBox.Value = BpsToKbps(s.GlobalUpBps);
            AltDownBox.Value = BpsToKbps(s.AltDownBps);
            AltUpBox.Value = BpsToKbps(s.AltUpBps);
            AltEnabledToggle.IsOn = s.AltEnabled;
            SchedulerEnabledToggle.IsOn = s.SchedulerEnabled;
            SchedulerDaysBox.SelectedIndex = (int)s.SchedulerDays;
            SchedulerStartPicker.SelectedTime = s.SchedulerStartTime.ToTimeSpan();
            SchedulerEndPicker.SelectedTime = s.SchedulerEndTime.ToTimeSpan();
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

    private async void OnSchedulerEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var enabled = SchedulerEnabledToggle.IsOn;
        await _settings.UpdateAsync(s => s.Speed.SchedulerEnabled = enabled);
    }

    private async void OnSchedulerDaysChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var days = (BandwidthScheduleDays)SchedulerDaysBox.SelectedIndex;
        await _settings.UpdateAsync(s => s.Speed.SchedulerDays = days);
    }

    private async void OnSchedulerStartChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        if (args.NewTime is null)
        {
            return;
        }
        var time = TimeOnly.FromTimeSpan(args.NewTime.Value);
        await _settings.UpdateAsync(s => s.Speed.SchedulerStartTime = time);
    }

    private async void OnSchedulerEndChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }
        if (args.NewTime is null)
        {
            return;
        }
        var time = TimeOnly.FromTimeSpan(args.NewTime.Value);
        await _settings.UpdateAsync(s => s.Speed.SchedulerEndTime = time);
    }
}
