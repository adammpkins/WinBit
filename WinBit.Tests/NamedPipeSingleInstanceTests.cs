using FluentAssertions;
using WinBit.Core.Shell;
using Xunit;

namespace WinBit.Tests;

public sealed class NamedPipeSingleInstanceTests
{
    // Mutex is re-entrant on the owning thread, so the "secondary" probe must run on a separate
    // thread for the test to mirror the cross-process behavior used in production.

    [Fact]
    public async Task First_acquirer_is_primary_second_is_secondary()
    {
        var name = UniqueName();
        using var primary = new NamedPipeSingleInstance(name);
        primary.TryAcquirePrimary().Should().BeTrue();
        primary.IsPrimary.Should().BeTrue();

        var secondaryIsPrimary = await Task.Run(() =>
        {
            using var secondary = new NamedPipeSingleInstance(name);
            return secondary.TryAcquirePrimary();
        });
        secondaryIsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task Forward_delivers_payload_to_primary_listener()
    {
        var name = UniqueName();
        using var primary = new NamedPipeSingleInstance(name);
        primary.TryAcquirePrimary().Should().BeTrue();

        var received = new TaskCompletionSource<string>();
        primary.StartListening(line => received.TrySetResult(line));

        var forwarded = await Task.Run(async () =>
        {
            using var secondary = new NamedPipeSingleInstance(name);
            secondary.TryAcquirePrimary().Should().BeFalse();
            return await secondary.ForwardAsync("magnet:?xt=urn:btih:abc", TimeSpan.FromSeconds(5));
        });
        forwarded.Should().BeTrue();

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        payload.Should().Be("magnet:?xt=urn:btih:abc");
    }

    [Fact]
    public async Task Forward_returns_false_when_no_primary_is_listening()
    {
        var name = UniqueName();
        using var solo = new NamedPipeSingleInstance(name);
        solo.TryAcquirePrimary().Should().BeTrue();
        // Deliberately skip StartListening — no server to connect to.

        var forwarded = await Task.Run(async () =>
        {
            using var client = new NamedPipeSingleInstance(name);
            client.TryAcquirePrimary().Should().BeFalse();
            return await client.ForwardAsync("anything", TimeSpan.FromMilliseconds(300));
        });
        forwarded.Should().BeFalse();
    }

    [Fact]
    public async Task Multiple_activations_each_reach_the_primary()
    {
        var name = UniqueName();
        using var primary = new NamedPipeSingleInstance(name);
        primary.TryAcquirePrimary().Should().BeTrue();

        var received = new List<string>();
        var gate = new object();
        const int expected = 3;
        var done = new TaskCompletionSource();
        primary.StartListening(line =>
        {
            lock (gate)
            {
                received.Add(line);
                if (received.Count == expected)
                {
                    done.TrySetResult();
                }
            }
        });

        for (int i = 0; i < expected; i++)
        {
            var payload = $"payload-{i}";
            var ok = await Task.Run(async () =>
            {
                using var secondary = new NamedPipeSingleInstance(name);
                secondary.TryAcquirePrimary().Should().BeFalse();
                return await secondary.ForwardAsync(payload, TimeSpan.FromSeconds(5));
            });
            ok.Should().BeTrue();
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Should().BeEquivalentTo(new[] { "payload-0", "payload-1", "payload-2" }, o => o.WithStrictOrdering());
    }

    private static string UniqueName() => $"WinBit.Tests.{Guid.NewGuid():N}";
}
