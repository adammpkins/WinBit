using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp.Alerts;
using LibtorrentSharp.Enums;
using Xunit;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Slice 2 of f-session-listen — typed `ListenSucceededAlert` /
/// `ListenFailedAlert` dispatch + the `GetListenInterfaces` getter.
/// </summary>
public sealed class SessionListenAlertTests
{
    [Fact]
    [Trait("Category", "Native")]
    public async Task FreshSession_emitsListenSucceededAlert()
    {
        using var session = new LibtorrentSession();

        // Spin up the iterator before the listener fully comes up so we don't
        // race past the alert. Bounded channel buffers any earlier alerts.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is ListenSucceededAlert succ)
                {
                    Assert.NotNull(succ.Address);
                    Assert.True(succ.Port > 0, $"Port was {succ.Port}");
                    // Default config opens TCP + UTP at minimum; we just need
                    // any one valid socket type to confirm the dispatch path.
                    Assert.True(Enum.IsDefined(typeof(SocketType), succ.SocketType),
                        $"unrecognized SocketType {succ.SocketType}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("ListenSucceededAlert didn't arrive within 10s of session construction.");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a ListenSucceededAlert arrived.");
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetListenInterfaces_returnsConfiguredValue_afterSet()
    {
        using var session = new LibtorrentSession();

        const string Configured = "127.0.0.1:0";
        session.SetListenInterfaces(Configured);

        // settings_pack apply is async — poll briefly for the new value.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.GetListenInterfaces() == Configured)
            {
                break;
            }
            Thread.Sleep(50);
        }

        Assert.Equal(Configured, session.GetListenInterfaces());
    }

    [Fact]
    [Trait("Category", "Native")]
    public void GetListenInterfaces_returnsNonEmpty_freshSession()
    {
        using var session = new LibtorrentSession();
        // Default config seeds listen_interfaces; should be non-empty.
        Assert.False(string.IsNullOrEmpty(session.GetListenInterfaces()));
    }

    [Fact]
    public void GetListenInterfaces_throwsAfterDispose()
    {
        var session = new LibtorrentSession();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.GetListenInterfaces());
    }

    [Fact]
    [Trait("Category", "Native")]
    public async Task ListenFailedAlert_dispatches_onUnreachableInterface()
    {
        using var session = new LibtorrentSession();

        // Drain the initial bring-up alerts first so we can isolate the
        // failure alert from the success alerts that fire on the default
        // bind. Just MoveNext once or twice with a short deadline.
        using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var draining = session.Alerts.GetAsyncEnumerator(drainCts.Token);
        try
        {
            while (await draining.MoveNextAsync())
            {
                // discard
                if (draining.Current is ListenSucceededAlert) break;
            }
        }
        catch (OperationCanceledException) { }

        // Now ask libtorrent to bind to a deliberately-unreachable address.
        // 192.0.2.x is RFC 5737 TEST-NET-1 — guaranteed not to be a local
        // interface, so libtorrent's bind() will fail and post a
        // listen_failed_alert.
        session.SetListenInterfaces("192.0.2.1:0");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var enumerator = session.Alerts.GetAsyncEnumerator(cts.Token);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                if (enumerator.Current is ListenFailedAlert fail)
                {
                    Assert.NotNull(fail.Interface);
                    Assert.NotNull(fail.ErrorMessage);
                    // Don't assert on specific error code — varies by OS — just
                    // confirm the dispatch shape carried real data through.
                    Assert.True(fail.ErrorCode != 0 || !string.IsNullOrEmpty(fail.ErrorMessage),
                        "ListenFailedAlert should carry either a non-zero ErrorCode or a non-empty ErrorMessage");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("ListenFailedAlert didn't arrive within 10s of binding to 192.0.2.1:0.");
        }
        finally
        {
            await enumerator.DisposeAsync();
            await draining.DisposeAsync();
        }

        Assert.Fail("Alert stream completed before a ListenFailedAlert arrived.");
    }
}
