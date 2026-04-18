using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinBit.ViewModels.Shell;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private NavItem? selectedItem;

    public ObservableCollection<NavItem> Items { get; } = new()
    {
        new NavItem("transfers", "Transfers", "\uE896"),
        new NavItem("rss", "RSS", "\uE704"),
        new NavItem("search", "Search", "\uE721"),
        new NavItem("logs", "Logs", "\uE756"),
        new NavItem("creator", "Torrent creator", "\uE8F1"),
        new NavItem("stats", "Statistics", "\uE9D9"),
        new NavItem("settings", "Settings", "\uE713"),
    };

    public ShellViewModel()
    {
        SelectedItem = Items[0];
    }
}

public sealed record NavItem(string Tag, string Label, string Glyph);
