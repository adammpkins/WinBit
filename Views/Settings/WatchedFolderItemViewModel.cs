namespace WinBit.Views.Settings;

internal sealed class WatchedFolderItemViewModel
{
    public required string Path { get; init; }
    public required string Description { get; init; }

    // Exposed so the Remove button's Tag binding can hand back the original path unchanged.
    public required string RawPath { get; init; }
}
