using FluentAssertions;
using MonoTorrent.Client;
using WinBit.Core.BitTorrent;
using Xunit;

namespace WinBit.Tests;

public sealed class TorrentErrorFormatterTests
{
    [Fact]
    public void Null_error_returns_null()
    {
        TorrentErrorFormatter.Format(null).Should().BeNull();
    }

    [Fact]
    public void Maps_reason_to_friendly_prefix_and_appends_exception_message()
    {
        var err = new Error(Reason.WriteFailure, new IOException("No space left on device"));
        TorrentErrorFormatter.Format(err).Should().Be("Disk write failure: No space left on device");
    }

    [Fact]
    public void Read_failure_uses_read_prefix()
    {
        var err = new Error(Reason.ReadFailure, new IOException("Bad sector"));
        TorrentErrorFormatter.Format(err).Should().Be("Disk read failure: Bad sector");
    }

    [Fact]
    public void Falls_back_to_reason_alone_when_exception_has_no_message()
    {
        var err = new Error(Reason.WriteFailure, new Exception(""));
        TorrentErrorFormatter.Format(err).Should().Be("Disk write failure");
    }
}
