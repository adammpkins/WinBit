using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// Phase F end-to-end check for the BEP44 immutable round-trip wired across
/// f-session-dht slices 3–5. Spins up a DHT-enabled session, puts a fresh
/// random blob, awaits <see cref="DhtPutAlert"/> on the live DHT, then
/// looks the same target up via <see cref="LibtorrentSession.DhtGetImmutable"/>
/// and asserts the bytes round-trip via <see cref="DhtImmutableItemAlert"/>.
/// Gated behind the standard <c>WINBIT_NETWORK_TESTS=1</c> opt-in so the
/// default test run stays fast (and offline).
/// </summary>
public sealed class DhtImmutableRoundTripTests
{
    [Fact]
    [Trait("Category", "Network")]
    public async Task PutThenGet_RoundTripsBlob_ViaPublicDht()
    {
        if (!NetworkTestGate.ShouldRun())
        {
            return;
        }

        // Use a unique payload per run so previous runs don't poison the result.
        var payload = Encoding.ASCII.GetBytes($"libtorrentsharp-roundtrip-{Guid.NewGuid():N}");

        var pack = new SettingsPack();
        pack.Set("alert_mask", (int)(AlertCategories.Status | AlertCategories.DHT));
        pack.Set("listen_interfaces", "0.0.0.0:0,[::]:0");
        pack.Set("enable_dht", true);
        pack.Set("enable_lsd", false);
        pack.Set("enable_upnp", false);
        pack.Set("enable_natpmp", false);
        pack.Set("dht_bootstrap_nodes",
            "router.bittorrent.com:6881,router.utorrent.com:6881,dht.transmissionbt.com:6881");

        using var client = new LibtorrentSession(pack);

        // Spin up the alert reader BEFORE issuing operations so we don't miss
        // either alert. Single-consumer Channel buffers everything in order.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var enumerator = client.Alerts.GetAsyncEnumerator(cts.Token);

        var putTarget = client.DhtPutImmutable(payload);
        Assert.False(putTarget.IsZero, "DhtPutImmutable returned the zero hash.");

        DhtPutAlert? putAlert = null;
        DhtImmutableItemAlert? getAlert = null;
        var getRequested = false;

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                switch (enumerator.Current)
                {
                    case DhtPutAlert pa when pa.Target == putTarget:
                        putAlert = pa;
                        // Trigger the lookup once the put completes — gives the
                        // store a moment to propagate before we ask for it back.
                        if (!getRequested)
                        {
                            client.DhtGetImmutable(putTarget);
                            getRequested = true;
                        }
                        break;

                    case DhtImmutableItemAlert ia when ia.Target == putTarget:
                        getAlert = ia;
                        break;
                }

                if (putAlert is not null && getAlert is not null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"Round-trip timed out: putAlert={putAlert is not null}, getAlert={getAlert is not null}");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.NotNull(putAlert);
        Assert.NotNull(getAlert);
        Assert.Equal(payload, getAlert!.Data.ToArray());
        Assert.True(putAlert!.NumSuccess >= 0, $"DhtPutAlert.NumSuccess was {putAlert.NumSuccess}");
    }
}
