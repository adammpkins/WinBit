using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MonoTorrent.Dht;
using WinBit.Core.Logging;

namespace WinBit.Core.BitTorrent;

/// <summary>
/// Seeds MonoTorrent's DHT routing table with a diverse set of bootstrap nodes on
/// engine start. Works around the long-running decay of canonical DHT bootstrap hosts
/// (router.bittorrent.com / router.utorrent.com unresponsive since late 2024, Transmission
/// host only partially answering) by giving the engine multiple independent operators
/// to race — "first to answer wins". Once any session reaches <c>Ready</c>, MonoTorrent's
/// own <c>AutoSaveLoadDhtCache</c> persists the routing table and subsequent starts rarely
/// need this path.
/// </summary>
public static class DhtBootstrapSeeder
{
    private const int CompactNodeLength = 26;
    private const int DefaultDhtPort = 6881;
    private static readonly TimeSpan TotalResolveBudget = TimeSpan.FromSeconds(3);

    public static async Task<SeedResult> InjectAsync(
        IDhtEngine dht,
        IReadOnlyList<string> hostSpecs,
        ILogService log,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dht);
        ArgumentNullException.ThrowIfNull(hostSpecs);
        ArgumentNullException.ThrowIfNull(log);

        if (hostSpecs.Count == 0)
        {
            log.Write("DHT seed: no bootstrap hosts configured — skipping", LogSeverity.Warning);
            return new SeedResult(0, Array.Empty<string>(), Array.Empty<(string, string)>());
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalResolveBudget);

        var resolved = new ConcurrentBag<(string Host, IPEndPoint Endpoint)>();
        var failed = new ConcurrentBag<(string Host, string Error)>();

        var stopwatch = Stopwatch.StartNew();
        var tasks = hostSpecs
            .Select(spec => ResolveOneAsync(spec, resolved, failed, budget.Token))
            .ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Per-host errors land in `failed`; Task.WhenAll throws when any task cancels or
            // faults. Swallowing lets us use whatever we successfully resolved within budget.
        }
        stopwatch.Stop();

        var endpoints = resolved.Select(r => r.Endpoint).Distinct().ToArray();
        var resolvedHosts = resolved.Select(r => r.Host).Distinct().ToArray();
        var failures = failed.ToArray();

        if (endpoints.Length == 0)
        {
            var failureSummary = failures.Length == 0
                ? "no A records returned within budget"
                : string.Join(", ", failures.Select(f => $"{f.Host}={f.Error}"));
            log.Write(
                $"DHT seed: injected 0 nodes from 0/{hostSpecs.Count} hosts — {failureSummary}",
                LogSeverity.Warning);
            return new SeedResult(0, resolvedHosts, failures);
        }

        var buffer = new byte[endpoints.Length * CompactNodeLength];
        for (var i = 0; i < endpoints.Length; i++)
        {
            EncodeCompactNode(endpoints[i], buffer.AsSpan(i * CompactNodeLength, CompactNodeLength));
        }

        dht.Add(new[] { (ReadOnlyMemory<byte>)buffer });

        var failSummary = failures.Length == 0
            ? string.Empty
            : $" (failed: {string.Join(", ", failures.Select(f => $"{f.Host}={f.Error}"))})";
        log.Write(
            $"DHT seed: injected {endpoints.Length} nodes from {resolvedHosts.Length}/{hostSpecs.Count} hosts "
            + $"in {stopwatch.ElapsedMilliseconds}ms{failSummary}",
            LogSeverity.Info);

        return new SeedResult(endpoints.Length, resolvedHosts, failures);
    }

    private static async Task ResolveOneAsync(
        string spec,
        ConcurrentBag<(string Host, IPEndPoint Endpoint)> resolved,
        ConcurrentBag<(string Host, string Error)> failed,
        CancellationToken ct)
    {
        if (!TryParseHostSpec(spec, out var host, out var port))
        {
            failed.Add((spec, "invalid host spec"));
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            failed.Add((host, "timeout"));
            return;
        }
        catch (Exception ex)
        {
            failed.Add((host, ex.GetType().Name));
            return;
        }

        var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (ipv4.Length == 0)
        {
            failed.Add((host, "no-ipv4"));
            return;
        }

        foreach (var addr in ipv4)
        {
            resolved.Add((host, new IPEndPoint(addr, port)));
        }
    }

    internal static byte[] EncodeCompactNode(IPEndPoint endpoint)
    {
        var buffer = new byte[CompactNodeLength];
        EncodeCompactNode(endpoint, buffer);
        return buffer;
    }

    internal static void EncodeCompactNode(IPEndPoint endpoint, Span<byte> buffer)
    {
        if (endpoint.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Compact-node format supports IPv4 only", nameof(endpoint));
        }
        if (buffer.Length < CompactNodeLength)
        {
            throw new ArgumentException($"Destination must be at least {CompactNodeLength} bytes", nameof(buffer));
        }

        RandomNumberGenerator.Fill(buffer[..20]);
        var addrBytes = endpoint.Address.GetAddressBytes();
        addrBytes.CopyTo(buffer[20..24]);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[24..26], (ushort)endpoint.Port);
    }

    internal static bool TryParseHostSpec(string? spec, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultDhtPort;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var trimmed = spec.Trim();
        var colonIdx = trimmed.LastIndexOf(':');
        if (colonIdx < 0)
        {
            host = trimmed;
            return true;
        }

        if (colonIdx == 0)
        {
            return false;
        }

        var hostPart = trimmed[..colonIdx];
        var portPart = trimmed[(colonIdx + 1)..];

        if (!int.TryParse(portPart, out var parsedPort) || parsedPort < 1 || parsedPort > 65535)
        {
            return false;
        }

        host = hostPart;
        port = parsedPort;
        return true;
    }
}

public readonly record struct SeedResult(
    int NodesInjected,
    IReadOnlyList<string> ResolvedHosts,
    IReadOnlyList<(string Host, string Error)> FailedHosts);
