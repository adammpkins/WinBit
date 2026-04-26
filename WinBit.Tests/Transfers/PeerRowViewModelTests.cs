using FluentAssertions;
using WinBit.Core.BitTorrent;
using Xunit;

namespace WinBit.Tests.Transfers;

/// <summary>
/// Validates the formatting and flag-building logic exposed by <see cref="PeerInfoFormatter"/>.
/// <c>PeerRowViewModel</c> delegates all formatting to that static class; tests here exercise
/// the production code directly rather than a copy.
/// </summary>
public sealed class PeerRowViewModelTests
{
    // ----- BuildFlags -----

    [Fact]
    public void BuildFlags_neither_seeder_nor_encrypted_returns_dash()
    {
        var info = MakePeer(isSeeder: false, isEncrypted: false);
        PeerInfoFormatter.BuildFlags(info).Should().Be("—");
    }

    [Fact]
    public void BuildFlags_seeder_only_returns_S()
    {
        var info = MakePeer(isSeeder: true, isEncrypted: false);
        PeerInfoFormatter.BuildFlags(info).Should().Be("S");
    }

    [Fact]
    public void BuildFlags_encrypted_only_returns_E()
    {
        var info = MakePeer(isSeeder: false, isEncrypted: true);
        PeerInfoFormatter.BuildFlags(info).Should().Be("E");
    }

    [Fact]
    public void BuildFlags_both_seeder_and_encrypted_returns_SE()
    {
        var info = MakePeer(isSeeder: true, isEncrypted: true);
        PeerInfoFormatter.BuildFlags(info).Should().Be("SE");
    }

    // ----- FormatSpeed -----

    [Fact]
    public void FormatSpeed_zero_returns_dash()
    {
        PeerInfoFormatter.FormatSpeed(0).Should().Be("—");
    }

    [Fact]
    public void FormatSpeed_negative_returns_dash()
    {
        PeerInfoFormatter.FormatSpeed(-1).Should().Be("—");
    }

    [Fact]
    public void FormatSpeed_1536_bytes_formats_as_kb()
    {
        PeerInfoFormatter.FormatSpeed(1536).Should().Be("1.5 KB/s");
    }

    [Fact]
    public void FormatSpeed_1_mb_and_a_half_formats_as_mb()
    {
        // 1.5 * 1048576 = 1572864
        PeerInfoFormatter.FormatSpeed(1_572_864).Should().Be("1.5 MB/s");
    }

    [Fact]
    public void FormatSpeed_small_bytes_formats_raw()
    {
        PeerInfoFormatter.FormatSpeed(512).Should().Be("512 B/s");
    }

    [Fact]
    public void FormatSpeed_gb_range_formats_as_gb()
    {
        // 2 GiB
        PeerInfoFormatter.FormatSpeed(2_147_483_648).Should().Be("2.0 GB/s");
    }

    // ----- PeerInfo field round-trip (spec contract for Update behavior) -----

    [Fact]
    public void PeerInfo_address_propagates()
    {
        var info = MakePeer(address: "192.0.2.1:6881");
        info.Address.Should().Be("192.0.2.1:6881");
    }

    [Fact]
    public void PeerInfo_null_client_stays_null()
    {
        var info = MakePeer(client: null);
        info.Client.Should().BeNull();
    }

    [Fact]
    public void PeerInfo_progress_0_to_1()
    {
        var info = MakePeer(progress: 0.75);
        info.Progress.Should().BeApproximately(0.75, 0.001);
    }

    // ----- Factory helper -----

    private static PeerInfo MakePeer(
        string address = "1.2.3.4:6881",
        string? client = null,
        double progress = 0.0,
        long downloadBps = 0,
        long uploadBps = 0,
        bool isSeeder = false,
        bool isEncrypted = false) => new PeerInfo
        {
            Address = address,
            Client = client,
            Progress = progress,
            DownloadSpeedBps = downloadBps,
            UploadSpeedBps = uploadBps,
            IsSeeder = isSeeder,
            IsEncrypted = isEncrypted,
        };
}
