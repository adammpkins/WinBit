using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using WinBit.Core.Logging;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Logs;

/// <summary>
/// Backs the Execution Log page. Subscribes to <see cref="ILogService.MessageLogged"/>, marshals
/// each entry to the UI thread via <see cref="IDispatcherQueueProvider"/>, and appends to a
/// capped <see cref="ObservableCollection{T}"/>. Severity filter flags reshape via
/// <see cref="AdvancedCollectionView.Filter"/> without rebuilding the backing list.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject, IDisposable
{
    /// <summary>Soft cap for in-memory rows. Older entries drop off the top as new ones arrive.</summary>
    public const int MaxVisibleRows = 2_000;

    private readonly ILogService _log;
    private readonly IDispatcherQueueProvider _dispatcher;
    private readonly ObservableCollection<LogRowViewModel> _rows = new();

    [ObservableProperty]
    private bool showNormal = true;

    [ObservableProperty]
    private bool showInfo = true;

    [ObservableProperty]
    private bool showWarning = true;

    [ObservableProperty]
    private bool showCritical = true;

    public AdvancedCollectionView Rows { get; }

    public LogsViewModel(ILogService log, IDispatcherQueueProvider dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;

        Rows = new AdvancedCollectionView(_rows, isLiveShaping: true)
        {
            Filter = PassesSeverityFilter,
        };

        foreach (var entry in _log.GetMessages())
        {
            _rows.Add(new LogRowViewModel(entry));
        }

        _log.MessageLogged += OnMessageLogged;
    }

    public void ClearView()
    {
        _rows.Clear();
    }

    public void Dispose()
    {
        _log.MessageLogged -= OnMessageLogged;
    }

    partial void OnShowNormalChanged(bool value) => Rows.RefreshFilter();
    partial void OnShowInfoChanged(bool value) => Rows.RefreshFilter();
    partial void OnShowWarningChanged(bool value) => Rows.RefreshFilter();
    partial void OnShowCriticalChanged(bool value) => Rows.RefreshFilter();

    private bool PassesSeverityFilter(object? item) =>
        item is LogRowViewModel row && row.Entry.Severity switch
        {
            LogSeverity.Normal => ShowNormal,
            LogSeverity.Info => ShowInfo,
            LogSeverity.Warning => ShowWarning,
            LogSeverity.Critical => ShowCritical,
            _ => true,
        };

    private void OnMessageLogged(object? sender, LogEntry entry)
    {
        _dispatcher.Enqueue(() =>
        {
            _rows.Add(new LogRowViewModel(entry));
            while (_rows.Count > MaxVisibleRows)
            {
                _rows.RemoveAt(0);
            }
        });
    }
}
