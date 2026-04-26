using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// c-resume — full resume round-trip against a live swarm: add magnet, let metadata
/// resolve, request resume data, dispose the session, then re-attach with the blob
/// on a fresh session and assert libtorrent skips the full re-check.
///
/// Lives in its own class (no IClassFixture) because the test deliberately tears
/// down the first session in the middle of the run. Gated through
/// <see cref="NetworkTestGate"/> like the rest of Phase C.
/// </summary>
[Trait("Category", "Network")]
public sealed class ResumeRoundTripTests
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResumeBlobTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PostReattachStableWindow = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task RequestResumeData_then_AttachTorrentWithResume_skips_recheck()
    {
        if (!NetworkTestGate.ShouldRun())
        {
            return;
        }

        var savePath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests-Resume", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(savePath);

        byte[] resumeBlob;
        try
        {
            // ---- First session: add the magnet, let metadata land, capture a blob.
            using (var firstClient = NewClient(savePath))
            {
                var magnet = firstClient.Add(new AddTorrentParams { MagnetUri = PublicSwarmFixture.UbuntuMagnetUri, SavePath = savePath }).Magnet!;
                Assert.True(magnet.IsValid);
                magnet.Resume();

                await WaitForMetadata(magnet, MetadataTimeout);

                resumeBlob = await CaptureResumeBlobAsync(firstClient, magnet, ResumeBlobTimeout);
            }

            Assert.NotEmpty(resumeBlob);

            // ---- Second session: re-attach with the blob, assert no re-check.
            using var secondClient = NewClient(savePath);
            var reattached = secondClient.Add(new AddTorrentParams { ResumeData = resumeBlob, SavePath = savePath }).Magnet!;
            Assert.True(reattached.IsValid);

            // Poll for the post-reattach window. If libtorrent decided to re-check,
            // the state will land in CheckingFiles / CheckingResume for a stretch.
            // A successful resume goes straight to Downloading / DownloadingMetadata
            // / Seeding / Finished and stays out of the checking states.
            var deadline = DateTimeOffset.UtcNow.Add(PostReattachStableWindow);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var state = reattached.GetCurrentStatus().State;
                Assert.False(
                    state is TorrentState.CheckingFiles or TorrentState.CheckingResume,
                    $"Re-attached torrent unexpectedly entered {state} state — resume blob did not skip the re-check.");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        finally
        {
            try { Directory.Delete(savePath, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static LibtorrentSession NewClient(string defaultDownloadPath)
    {
        var pack = new SettingsPack();
        pack.Set("alert_mask", (int)AlertCategories.Status | (int)AlertCategories.Storage);
        pack.Set("listen_interfaces", "0.0.0.0:0,[::]:0");
        pack.Set("enable_dht", true);
        pack.Set("enable_lsd", false);
        pack.Set("enable_upnp", false);
        pack.Set("enable_natpmp", false);
        pack.Set("dht_bootstrap_nodes",
            "router.bittorrent.com:6881,router.utorrent.com:6881,dht.transmissionbt.com:6881");

        return new LibtorrentSession(pack)
        {
            DefaultDownloadPath = defaultDownloadPath,
        };
    }

    private static async Task WaitForMetadata(MagnetHandle magnet, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (magnet.GetCurrentStatus().State != TorrentState.DownloadingMetadata)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        Assert.Fail($"Metadata didn't resolve within {timeout.TotalSeconds:0}s.");
    }

    private static async Task<byte[]> CaptureResumeBlobAsync(LibtorrentSession client, MagnetHandle magnet, TimeSpan timeout)
    {
        // Spin up the iterator BEFORE issuing the request so we don't miss the alert.
        // The single-consumer Channel under Alerts buffers anything that arrives
        // before MoveNextAsync, but only one iterator may run at a time.
        using var cts = new CancellationTokenSource(timeout);
        var enumerator = client.Alerts.GetAsyncEnumerator(cts.Token);

        client.RequestResumeData(magnet);

        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                if (enumerator.Current is ResumeDataReadyAlert resume)
                {
                    return resume.ResumeData ?? Array.Empty<byte>();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"ResumeDataReadyAlert didn't arrive within {timeout.TotalSeconds:0}s.");
            throw; // unreachable
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        Assert.Fail("Alert stream completed before a ResumeDataReadyAlert arrived.");
        throw new InvalidOperationException("unreachable");
    }
}
