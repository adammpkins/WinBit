using CommunityToolkit.Mvvm.ComponentModel;
using WinBit.Core.Search;

namespace WinBit.ViewModels.Search;

/// <summary>Presentation layer for one <see cref="SearchResult"/> rendered in the Search page's list.</summary>
public sealed partial class SearchHitRowViewModel : ObservableObject
{
    public SearchHitRowViewModel(SearchResult hit)
    {
        Hit = hit;
        Name = hit.Name;
        PluginName = hit.PluginName;
        SizeText = FormatSize(hit.SizeBytes);
        SeedersText = hit.Seeders?.ToString("N0") ?? "—";
        LeechersText = hit.Leechers?.ToString("N0") ?? "—";
        CanAdd = !string.IsNullOrEmpty(hit.MagnetUri) || !string.IsNullOrEmpty(hit.TorrentUrl);
    }

    public SearchResult Hit { get; }

    public string Name { get; }
    public string PluginName { get; }
    public string SizeText { get; }
    public string SeedersText { get; }
    public string LeechersText { get; }
    public bool CanAdd { get; }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null || bytes <= 0)
        {
            return "—";
        }
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes.Value;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return $"{v:0.#} {units[u]}";
    }
}
