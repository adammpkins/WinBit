using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;

namespace WinBit.Views.Settings;

public sealed partial class BehaviorPage : Page
{
    private readonly ISettingsService _settings;
    private bool _loading;

    public BehaviorPage()
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
            CloseToTrayToggle.IsOn = _settings.Current.Behavior.CloseToTray;
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnCloseToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var value = CloseToTrayToggle.IsOn;
        await _settings.UpdateAsync(s => s.Behavior.CloseToTray = value);
    }
}
