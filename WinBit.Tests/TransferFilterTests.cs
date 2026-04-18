using FluentAssertions;
using WinBit.Core.BitTorrent;
using WinBit.Core.Filters;
using Xunit;

namespace WinBit.Tests;

public sealed class TransferFilterTests
{
    private static TransferFilterInputs Inputs(
        string? category = null,
        IReadOnlyList<string>? tags = null,
        TorrentState state = TorrentState.Stopped,
        double progress = 0,
        long downBps = 0,
        long upBps = 0,
        IReadOnlyList<string>? trackerHosts = null) =>
        new(category, tags ?? Array.Empty<string>(), state, progress, downBps, upBps, trackerHosts);

    [Fact]
    public void All_matches_every_row()
    {
        var filter = TransferFilter.All;
        filter.Matches(Inputs()).Should().BeTrue();
        filter.Matches(Inputs("linux", new[] { "iso" }, TorrentState.Downloading, 0.5, 1000, 0)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Uncategorized_matches_rows_without_a_category(string? category)
    {
        TransferFilter.Uncategorized.Matches(Inputs(category: category)).Should().BeTrue();
    }

    [Fact]
    public void Uncategorized_rejects_rows_with_a_category()
    {
        TransferFilter.Uncategorized.Matches(Inputs(category: "music")).Should().BeFalse();
    }

    [Fact]
    public void Category_matches_case_insensitively()
    {
        var filter = TransferFilter.ForCategory("Linux");
        filter.Matches(Inputs(category: "linux")).Should().BeTrue();
        filter.Matches(Inputs(category: "LINUX")).Should().BeTrue();
        filter.Matches(Inputs(category: "music")).Should().BeFalse();
        filter.Matches(Inputs(category: null)).Should().BeFalse();
    }

    [Fact]
    public void Tag_matches_case_insensitively_across_the_tag_list()
    {
        var filter = TransferFilter.ForTag("iso");
        filter.Matches(Inputs(category: "anything", tags: new[] { "archive", "ISO" })).Should().BeTrue();
        filter.Matches(Inputs(category: "anything", tags: new[] { "archive" })).Should().BeFalse();
        filter.Matches(Inputs()).Should().BeFalse();
    }

    [Fact]
    public void Status_Downloading_matches_only_downloading_state()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Downloading);
        filter.Matches(Inputs(state: TorrentState.Downloading)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Seeding)).Should().BeFalse();
        filter.Matches(Inputs(state: TorrentState.Paused)).Should().BeFalse();
    }

    [Fact]
    public void Status_Seeding_matches_only_seeding_state()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Seeding);
        filter.Matches(Inputs(state: TorrentState.Seeding)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Downloading)).Should().BeFalse();
    }

    [Fact]
    public void Status_Completed_is_progress_based_not_state_based()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Completed);
        filter.Matches(Inputs(state: TorrentState.Seeding, progress: 1.0)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Paused, progress: 1.0)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Downloading, progress: 0.99)).Should().BeFalse();
    }

    [Fact]
    public void Status_Paused_matches_paused_and_stopped()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Paused);
        filter.Matches(Inputs(state: TorrentState.Paused)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Stopped)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Seeding)).Should().BeFalse();
    }

    [Fact]
    public void Status_Active_requires_nonzero_rate()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Active);
        filter.Matches(Inputs(downBps: 1)).Should().BeTrue();
        filter.Matches(Inputs(upBps: 1)).Should().BeTrue();
        filter.Matches(Inputs()).Should().BeFalse();
    }

    [Fact]
    public void Status_Inactive_requires_zero_rate()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Inactive);
        filter.Matches(Inputs()).Should().BeTrue();
        filter.Matches(Inputs(downBps: 1)).Should().BeFalse();
        filter.Matches(Inputs(upBps: 1)).Should().BeFalse();
    }

    [Fact]
    public void Status_Errored_matches_only_error_state()
    {
        var filter = TransferFilter.ForStatus(TransferStatus.Errored);
        filter.Matches(Inputs(state: TorrentState.Error)).Should().BeTrue();
        filter.Matches(Inputs(state: TorrentState.Downloading)).Should().BeFalse();
    }

    [Fact]
    public void TrackerHost_matches_any_host_case_insensitively()
    {
        var filter = TransferFilter.ForTrackerHost("tracker.example.org");
        filter.Matches(Inputs(trackerHosts: new[] { "tracker.example.org" })).Should().BeTrue();
        filter.Matches(Inputs(trackerHosts: new[] { "TRACKER.EXAMPLE.ORG", "other.example" })).Should().BeTrue();
        filter.Matches(Inputs(trackerHosts: new[] { "other.example" })).Should().BeFalse();
        filter.Matches(Inputs()).Should().BeFalse();
    }
}
