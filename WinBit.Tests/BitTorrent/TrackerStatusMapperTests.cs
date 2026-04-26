using FluentAssertions;
using WinBit.Core.BitTorrent;
using Xunit;

namespace WinBit.Tests.BitTorrent;

public sealed class TrackerStatusMapperTests
{
    [Fact]
    public void Updating_true_returns_Updating_regardless_of_other_fields()
    {
        TrackerStatusMapper.MapStatus(updating: true, fails: 0, lastError: null, verified: false)
            .Should().Be(TrackerStatus.Updating);
    }

    [Fact]
    public void Updating_true_wins_over_failure_signals()
    {
        TrackerStatusMapper.MapStatus(updating: true, fails: 5, lastError: "err", verified: true)
            .Should().Be(TrackerStatus.Updating);
    }

    [Fact]
    public void Nonzero_fails_returns_Failure()
    {
        TrackerStatusMapper.MapStatus(updating: false, fails: 1, lastError: null, verified: false)
            .Should().Be(TrackerStatus.Failure);
    }

    [Fact]
    public void Nonempty_lastError_returns_Failure()
    {
        TrackerStatusMapper.MapStatus(updating: false, fails: 0, lastError: "timeout", verified: false)
            .Should().Be(TrackerStatus.Failure);
    }

    [Fact]
    public void Verified_with_no_failures_returns_Working()
    {
        TrackerStatusMapper.MapStatus(updating: false, fails: 0, lastError: null, verified: true)
            .Should().Be(TrackerStatus.Working);
    }

    [Fact]
    public void No_contact_signals_returns_NotContacted()
    {
        TrackerStatusMapper.MapStatus(updating: false, fails: 0, lastError: null, verified: false)
            .Should().Be(TrackerStatus.NotContacted);
    }
}
