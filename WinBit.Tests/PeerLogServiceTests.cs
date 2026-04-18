using FluentAssertions;
using WinBit.Core.Logging;
using Xunit;

namespace WinBit.Tests;

public sealed class PeerLogServiceTests
{
    [Fact]
    public void Record_appends_entries_and_fires_event()
    {
        var service = new PeerLogService();
        var fired = new List<PeerLogEntry>();
        service.EntryAdded += (_, e) => fired.Add(e);

        service.Record("203.0.113.7:12345", "Blocked by IP filter");

        service.Recent.Should().ContainSingle()
            .Which.Reason.Should().Be("Blocked by IP filter");
        fired.Should().ContainSingle()
            .Which.PeerEndpoint.Should().Be("203.0.113.7:12345");
    }

    [Fact]
    public void Capacity_overflow_drops_oldest_entries()
    {
        var service = new PeerLogService();
        for (var i = 0; i < PeerLogService.Capacity + 10; i++)
        {
            service.Record($"10.0.0.{i % 256}:6881", $"entry {i}");
        }

        service.Recent.Should().HaveCount(PeerLogService.Capacity);
        // The oldest ~10 entries were evicted.
        service.Recent.First().Reason.Should().NotBe("entry 0");
    }
}
