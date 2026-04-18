using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Settings;
using WinBit.Services;
using WinBit.ViewModels.Transfers;
using WinUI.TableView;

namespace WinBit.Views.Transfers;

public sealed partial class TransfersPage : Page
{
    private readonly ISettingsService _settings;

    public TransfersViewModel ViewModel { get; }

    public TransfersPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TransfersViewModel>();
        _settings = App.Services.GetRequiredService<ISettingsService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static Visibility VisibleIfTrue(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        ApplyLayout(_settings.Current.UiState.TransfersGrid);

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var layout = CaptureLayout();
        await _settings.UpdateAsync(s => s.UiState.TransfersGrid = layout);
    }

    private void ApplyLayout(TransfersGridLayout layout)
    {
        if (layout.Columns.Count == 0)
        {
            return;
        }

        foreach (var column in TransfersGrid.Columns)
        {
            if (column.Tag is not string tag || !layout.Columns.TryGetValue(tag, out var state))
            {
                continue;
            }

            if (state.Width > 0)
            {
                column.Width = new Microsoft.UI.Xaml.GridLength(state.Width);
            }

            column.Order = state.Order;

            column.SortDirection = state.SortDirection switch
            {
                "Ascending" => SortDirection.Ascending,
                "Descending" => SortDirection.Descending,
                _ => null,
            };
        }
    }

    private TransfersGridLayout CaptureLayout()
    {
        var layout = new TransfersGridLayout();

        foreach (var column in TransfersGrid.Columns)
        {
            if (column.Tag is not string tag)
            {
                continue;
            }

            layout.Columns[tag] = new TransferColumnState
            {
                Width = column.ActualWidth > 0 ? column.ActualWidth : 0,
                Order = column.Order ?? 0,
                SortDirection = column.SortDirection switch
                {
                    SortDirection.Ascending => "Ascending",
                    SortDirection.Descending => "Descending",
                    _ => null,
                },
            };
        }

        return layout;
    }

    private async void OnAddTorrentClicked(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<IDialogService>()
            .ShowAsync("Add torrent", "Torrent adding lands in milestone M4 — Add-Torrent dialog deliverable.");
    }

    private async void OnAddMagnetClicked(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<IDialogService>()
            .ShowAsync("Add magnet", "Magnet adding lands in milestone M4 — Add-Magnet dialog deliverable.");
    }
}
