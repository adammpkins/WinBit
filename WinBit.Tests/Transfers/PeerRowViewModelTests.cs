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
    public void BuildFlags_all_false_returns_K_question()
    {
        // A freshly connecting peer has no interest declared on either side and both sides
        // unchoked (the default libtorrent state), so K and ? both apply.
        var info = MakePeer();
        PeerInfoFormatter.BuildFlags(info).Should().Be("K?");
    }

    [Fact]
    public void BuildFlags_interesting_and_unchoked_returns_D()
    {
        var info = MakePeer(isInteresting: true, isChoked: false);
        // Also: !isRemoteChoked(false) && !isInteresting → K does NOT fire because isInteresting is true.
        // !isChoked(false) && !isRemoteInterested(false) → ? fires.
        PeerInfoFormatter.BuildFlags(info).Should().Be("D?");
    }

    [Fact]
    public void BuildFlags_interesting_and_remote_choked_returns_d()
    {
        // 'd' = we want pieces but the peer has choked us (IsRemoteChoked=true).
        var info = MakePeer(isInteresting: true, isRemoteChoked: true);
        // IsRemoteChoked=true suppresses K. IsRemoteInteresting=false, IsRemoteChoked=true → ? does not fire.
        PeerInfoFormatter.BuildFlags(info).Should().Be("d");
    }

    [Fact]
    public void BuildFlags_remote_interested_and_not_choked_returns_KU()
    {
        // K comes first (from the IsInteresting=false branch), then U.
        var info = MakePeer(isRemoteInteresting: true);
        // IsInteresting=false, !IsChoked=true → K fires first.
        // IsRemoteInteresting=true, !IsChoked=true → U fires.
        PeerInfoFormatter.BuildFlags(info).Should().Be("KU");
    }

    [Fact]
    public void BuildFlags_remote_interested_and_choked_returns_u()
    {
        // 'u' = the peer wants our pieces but we have choked it (IsChoked=true).
        var info = MakePeer(isRemoteInteresting: true, isChoked: true);
        // IsChoked=true suppresses K (K requires !IsChoked). IsRemoteInteresting=true, IsChoked=true → u.
        PeerInfoFormatter.BuildFlags(info).Should().Be("u");
    }

    [Fact]
    public void BuildFlags_incoming_connection_appends_I()
    {
        var info = MakePeer(isIncomingConnection: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("I");
    }

    [Fact]
    public void BuildFlags_rc4_encrypted_appends_E()
    {
        var info = MakePeer(isEncrypted: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("E");
    }

    [Fact]
    public void BuildFlags_plaintext_encrypted_appends_lowercase_e()
    {
        var info = MakePeer(isHandshakeEncrypted: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("e");
        PeerInfoFormatter.BuildFlags(info).Should().NotContain("E");
    }

    [Fact]
    public void BuildFlags_utp_appends_P()
    {
        var info = MakePeer(isUtp: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("P");
    }

    [Fact]
    public void BuildFlags_holepunched_appends_h()
    {
        var info = MakePeer(isHolepunched: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("h");
    }

    [Fact]
    public void BuildFlags_dht_appends_H()
    {
        var info = MakePeer(isFromDht: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("H");
    }

    [Fact]
    public void BuildFlags_pex_appends_X()
    {
        var info = MakePeer(isFromPex: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("X");
    }

    [Fact]
    public void BuildFlags_lsd_appends_L()
    {
        var info = MakePeer(isFromLsd: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("L");
    }

    [Fact]
    public void BuildFlags_seeder_has_no_S_flag()
    {
        // Seeder status is shown in the Progress/State column, not as a flags character.
        var info = MakePeer(isSeeder: true);
        PeerInfoFormatter.BuildFlags(info).Should().NotContain("S");
    }

    [Fact]
    public void BuildFlags_snubbed_appends_S()
    {
        var info = MakePeer(isSnubbed: true);
        // isSnubbed uses 'S'; isSeeder does not affect flags string
        PeerInfoFormatter.BuildFlags(info).Should().Contain("S");
    }

    [Fact]
    public void BuildFlags_combo_downloading_encrypted_utp_returns_correct_order()
    {
        // Interesting + unchoked (D), RC4 encrypted (E), uTP (P). No remote interest so ? fires too.
        var info = MakePeer(isInteresting: true, isChoked: false, isEncrypted: true, isUtp: true);
        PeerInfoFormatter.BuildFlags(info).Should().Be("D?EP");
    }

    [Fact]
    public void BuildFlags_optimistic_unchoke_appends_O()
    {
        var info = MakePeer(isOptimisticUnchoke: true);
        PeerInfoFormatter.BuildFlags(info).Should().Contain("O");
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
        bool isEncrypted = false,
        bool isHandshakeEncrypted = false,
        bool isInteresting = false,
        bool isChoked = false,
        bool isRemoteInteresting = false,
        bool isRemoteChoked = false,
        bool isOptimisticUnchoke = false,
        bool isSnubbed = false,
        bool isIncomingConnection = false,
        bool isFromDht = false,
        bool isFromPex = false,
        bool isFromLsd = false,
        bool isUtp = false,
        bool isHolepunched = false) => new PeerInfo
        {
            Address = address,
            Client = client,
            Progress = progress,
            DownloadSpeedBps = downloadBps,
            UploadSpeedBps = uploadBps,
            IsSeeder = isSeeder,
            IsEncrypted = isEncrypted,
            IsHandshakeEncrypted = isHandshakeEncrypted,
            IsInteresting = isInteresting,
            IsChoked = isChoked,
            IsRemoteInteresting = isRemoteInteresting,
            IsRemoteChoked = isRemoteChoked,
            IsOptimisticUnchoke = isOptimisticUnchoke,
            IsSnubbed = isSnubbed,
            IsIncomingConnection = isIncomingConnection,
            IsFromDht = isFromDht,
            IsFromPex = isFromPex,
            IsFromLsd = isFromLsd,
            IsUtp = isUtp,
            IsHolepunched = isHolepunched,
        };
}
