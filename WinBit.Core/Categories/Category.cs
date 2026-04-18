namespace WinBit.Core.Categories;

/// <summary>
/// Ports <c>BitTorrent::CategoryOptions</c> from
/// <c>qbittorrent/src/base/bittorrent/categoryoptions.h</c>. Share-limit and download-path
/// extensions land with their own deliverables; this first pass carries the category name
/// plus an optional save-path override used by TMM resolution in a later M5 deliverable.
/// </summary>
public sealed record Category
{
    public required string Name { get; init; }

    /// <summary>Save-path override when this category is assigned; null = use global default.</summary>
    public string? SavePath { get; init; }

    /// <summary>Incomplete-download path override; null = use global default.</summary>
    public string? DownloadPath { get; init; }
}
