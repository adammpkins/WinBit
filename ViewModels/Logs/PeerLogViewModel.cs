using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.Logging;
using WinBit.Infrastructure;

namespace WinBit.ViewModels.Logs;

/// <summary>
/// Backs the Peer log tab. Subscribes to <see cref="IPeerLogService.EntryAdded"/>, marshals
/// updates to the UI thread, and keeps a capped observable list. Singleton so the view survives
/// navigation without losing history.
/// </summary>
public sealed partial class PeerLogViewModel : ObservableObject
{
    public const int MaxVisibleRows = 2_000;

    private readonly IPeerLogService _service;
    private readonly IDispatcherQueueProvider _dispatcher;

    public ObservableCollection<PeerLogRowViewModel> Rows { get; } = new();

    public PeerLogViewModel(IPeerLogService service, IDispatcherQueueProvider dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;

        foreach (var entry in _service.Recent)
        {
            Rows.Add(new PeerLogRowViewModel(entry));
        }

        _service.EntryAdded += OnEntryAdded;
    }

    public void ClearView() => Rows.Clear();

    private void OnEntryAdded(object? sender, PeerLogEntry entry)
    {
        _dispatcher.Enqueue(() =>
        {
            Rows.Add(new PeerLogRowViewModel(entry));
            while (Rows.Count > MaxVisibleRows)
            {
                Rows.RemoveAt(0);
            }
        });
    }
}
