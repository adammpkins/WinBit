using WinBit.Core.BitTorrent;

namespace WinBit.Core.Filters;

public enum TransferFilterKind
{
    All,
    Uncategorized,
    Category,
    Tag,
    Status,
    TrackerHost,
}

/// <summary>
/// The seven status buckets surfaced on the status filter sidebar. Mirrors qBittorrent's
/// TransferListFiltersWidget status categories.
/// </summary>
public enum TransferStatus
{
    Downloading,
    Seeding,
    Completed,
    Paused,
    Active,
    Inactive,
    Errored,
}

/// <summary>
/// The inputs a filter evaluates against a single row. Collects every field any filter kind
/// might need so <see cref="TransferFilter.Matches"/> stays a one-liner per caller.
/// </summary>
public readonly record struct TransferFilterInputs(
    string? Category,
    IReadOnlyList<string> Tags,
    TorrentState State,
    double Progress,
    long DownloadSpeedBps,
    long UploadSpeedBps,
    IReadOnlyList<string>? TrackerHosts = null);

/// <summary>
/// A selector applied to the transfers grid — "All torrents", the uncategorized bucket, a
/// specific category name, a specific tag, or one of the seven status buckets. Matching is
/// case-insensitive. Pure data, no UI dependencies — lives in Core so it can be unit-tested.
/// </summary>
public sealed record TransferFilter(TransferFilterKind Kind, string? Name = null, TransferStatus Status = default)
{
    public static TransferFilter All { get; } = new(TransferFilterKind.All);

    public static TransferFilter Uncategorized { get; } = new(TransferFilterKind.Uncategorized);

    public static TransferFilter ForCategory(string name) => new(TransferFilterKind.Category, name);

    public static TransferFilter ForTag(string name) => new(TransferFilterKind.Tag, name);

    public static TransferFilter ForStatus(TransferStatus status) =>
        new(TransferFilterKind.Status, status.ToString(), status);

    public static TransferFilter ForTrackerHost(string host) => new(TransferFilterKind.TrackerHost, host);

    public bool Matches(TransferFilterInputs inputs) => Kind switch
    {
        TransferFilterKind.All => true,
        TransferFilterKind.Uncategorized => string.IsNullOrWhiteSpace(inputs.Category),
        TransferFilterKind.Category => string.Equals(inputs.Category, Name, StringComparison.OrdinalIgnoreCase),
        TransferFilterKind.Tag => Name is not null
            && inputs.Tags.Any(t => string.Equals(t, Name, StringComparison.OrdinalIgnoreCase)),
        TransferFilterKind.Status => MatchesStatus(inputs),
        TransferFilterKind.TrackerHost => Name is not null
            && inputs.TrackerHosts is { } hosts
            && hosts.Any(h => string.Equals(h, Name, StringComparison.OrdinalIgnoreCase)),
        _ => true,
    };

    private bool MatchesStatus(TransferFilterInputs inputs) => Status switch
    {
        TransferStatus.Downloading => inputs.State == TorrentState.Downloading,
        TransferStatus.Seeding => inputs.State == TorrentState.Seeding,
        // Completed = fully downloaded. Includes anything ≥ 100% regardless of current state so
        // paused-after-complete and actively seeding torrents both show up — matches
        // qBittorrent's TorrentImpl::isCompleted().
        TransferStatus.Completed => inputs.Progress >= 1.0,
        TransferStatus.Paused => inputs.State is TorrentState.Paused or TorrentState.Stopped,
        TransferStatus.Active => inputs.DownloadSpeedBps > 0 || inputs.UploadSpeedBps > 0,
        TransferStatus.Inactive => inputs.DownloadSpeedBps == 0 && inputs.UploadSpeedBps == 0,
        TransferStatus.Errored => inputs.State == TorrentState.Error,
        _ => false,
    };
}
