using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using MonoTorrent.Dht;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Diagnostic UDP probe that exercises the DHT network path and the engine's own DHT socket.
/// Off by default (<see cref="AdvancedSettings.EnableDhtNetworkProbe"/>); enable when filing
/// bug reports so the log captures Pass 1/2/3 lines. Three passes:
/// <list type="bullet">
///   <item>Pass 1 (eph): ephemeral source port, tests stateful-firewall return path.</item>
///   <item>Pass 2 (b6882): fixed bound port, mirrors MonoTorrent's persistent listener.</item>
///   <item>Pass 3 (lb): loopback to MonoTorrent's own DHT listener on <c>127.0.0.1</c>.
///     Fires on the first <c>DhtStateChanged</c> transition away from <c>NotReady</c> —
///     i.e., once the engine has actually bound its socket. Never racing the bind.</item>
/// </list>
/// </summary>
public sealed class DhtNetworkProbe : IHostedService
{
    private static readonly (string Host, int Port)[] BootstrapNodes = new[]
    {
        ("router.bittorrent.com", 6881),
        ("router.utorrent.com", 6881),
        ("dht.transmissionbt.com", 6881),
        ("router.bitcomet.com", 6881),
    };

    private readonly ILogService _log;
    private readonly ISettingsService _settings;
    private readonly ITorrentSessionService _session;

    public DhtNetworkProbe(ILogService log, ISettingsService settings, ITorrentSessionService session)
    {
        _log = log;
        _settings = settings;
        _session = session;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _ = Task.Run(() => RunAsync(ct), ct);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

            if (!_settings.Current.Advanced.EnableDhtNetworkProbe)
            {
                return;
            }

            // Pass 1: ephemeral source port. Exercises the return path with stateful-firewall
            // behavior (reply flows back to the source port of our outbound packet).
            foreach (var node in BootstrapNodes)
            {
                await ProbeAsync(node.Host, node.Port, localPort: 0, ct).ConfigureAwait(false);
            }

            // Pass 2: fixed local port 6882 (6881 is held by MonoTorrent's DHT). This mirrors
            // MonoTorrent's scenario — a persistent bound socket both sending and receiving.
            // If Pass 1 succeeds for a host and Pass 2 times out for the same host, something
            // is filtering return UDP to a fixed listener port (common VPN egress behavior).
            foreach (var node in BootstrapNodes)
            {
                await ProbeAsync(node.Host, node.Port, localPort: 6882, ct).ConfigureAwait(false);
            }

            // Pass 3: ping MonoTorrent's own DHT listener on 127.0.0.1:6881. The engine only
            // binds its listener once DHT leaves NotReady — fire Pass 3 on that transition,
            // never at a fixed delay.
            await WaitForDhtBindThenLoopbackAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown during probe - fine.
        }
        catch (Exception ex)
        {
            _log.Write($"DHT probe crashed: {ex.Message}", LogSeverity.Warning);
        }
    }

    private async Task WaitForDhtBindThenLoopbackAsync(CancellationToken ct)
    {
        if (_session.CurrentDhtState != DhtState.NotReady)
        {
            await ProbeAsync("127.0.0.1", 6881, localPort: 0, ct).ConfigureAwait(false);
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, EventArgs e)
        {
            if (_session.CurrentDhtState != DhtState.NotReady)
            {
                tcs.TrySetResult(true);
            }
        }

        _session.DhtStateChanged += Handler;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var _ = timeout.Token.Register(() => tcs.TrySetResult(false));

            var transitioned = await tcs.Task.ConfigureAwait(false);
            if (!transitioned)
            {
                _log.Write(
                    "DHT probe[lb] 127.0.0.1:6881 Pass-3 skipped — DHT never left NotReady within 60s",
                    LogSeverity.Warning);
                return;
            }
        }
        finally
        {
            _session.DhtStateChanged -= Handler;
        }

        await ProbeAsync("127.0.0.1", 6881, localPort: 0, ct).ConfigureAwait(false);
    }

    private async Task ProbeAsync(string host, int port, int localPort, CancellationToken ct)
    {
        var tag = localPort == 0 ? "eph" : $"b{localPort}";
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"DHT probe[{tag}] {host}:{port} dns-fail: {ex.Message}", LogSeverity.Warning);
            return;
        }

        var ipv4 = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is null)
        {
            _log.Write($"DHT probe[{tag}] {host}:{port} no-ipv4-address", LogSeverity.Warning);
            return;
        }

        using var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
        }
        catch (Exception ex)
        {
            _log.Write($"DHT probe[{tag}] {host}:{port} bind-fail: {ex.Message}", LogSeverity.Warning);
            return;
        }
        var localEp = (IPEndPoint)client.Client.LocalEndPoint!;
        var ping = BuildPing();

        try
        {
            await client.SendAsync(ping, ping.Length, new IPEndPoint(ipv4, port)).ConfigureAwait(false);
            _log.Write($"DHT probe[{tag}] {host}:{port} sent from local:{localEp.Port} bytes:{ping.Length}", LogSeverity.Info);
        }
        catch (Exception ex)
        {
            _log.Write($"DHT probe[{tag}] {host}:{port} send-fail: {ex.Message}", LogSeverity.Warning);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await client.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            _log.Write($"DHT probe[{tag}] {host}:{port} recv from:{result.RemoteEndPoint} bytes:{result.Buffer.Length}", LogSeverity.Info);
        }
        catch (OperationCanceledException)
        {
            _log.Write($"DHT probe[{tag}] {host}:{port} TIMEOUT — no response within 5s", LogSeverity.Warning);
        }
        catch (Exception ex)
        {
            _log.Write($"DHT probe[{tag}] {host}:{port} recv-fail: {ex.Message}", LogSeverity.Warning);
        }
    }

    // Minimal bencoded KRPC ping: d1:ad2:id20:<20 random bytes>e1:q4:ping1:t2:aa1:y1:qe
    private static byte[] BuildPing()
    {
        var nodeId = new byte[20];
        Random.Shared.NextBytes(nodeId);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        void WriteAscii(string s) => writer.Write(System.Text.Encoding.ASCII.GetBytes(s));

        WriteAscii("d1:ad2:id20:");
        writer.Write(nodeId);
        WriteAscii("e1:q4:ping1:t2:aa1:y1:qe");

        return ms.ToArray();
    }
}
