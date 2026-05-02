using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibtorrentSharp;
using LibtorrentSharp.Alerts;

namespace LibtorrentSharp.Tests;

/// <summary>
/// Eagerly drains a <see cref="LibtorrentSession"/>'s async alert stream
/// into a concurrent snapshot, decoupling alert arrival from test-side
/// await. Fixes a race observed in the AddTorrentAlert test
/// where the alert fired (and was written to the session's channel)
/// before the test reached its <c>WaitForAlertAsync</c> call — under
/// specific scheduler conditions the alert would sit in the channel
/// until the iterator was constructed, but in the failing-case path
/// something about the buffered-channel iteration apparently didn't
/// surface it in time.
/// <para>
/// The pump starts the instant the capture is constructed, so consumers
/// should instantiate before issuing any action that would fire alerts
/// (i.e. before <c>Add()</c> in the loopback fixture).
/// </para>
/// </summary>
public sealed class AlertCapture : IDisposable
{
    private readonly ConcurrentQueue<Alert> _captured = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;
    private bool _disposed;

    public AlertCapture(LibtorrentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _pumpTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var alert in session.Alerts.WithCancellation(_cts.Token))
                {
                    _captured.Enqueue(alert);
                }
            }
            catch (OperationCanceledException)
            {
                // expected on Dispose
            }
        });
    }

    /// <summary>Snapshot of every alert captured so far, in arrival order.</summary>
    public IReadOnlyList<Alert> Snapshot() => _captured.ToArray();

    /// <summary>
    /// Polls the capture for an alert of type <typeparamref name="T"/> matching
    /// <paramref name="predicate"/>, returning it when found or <c>null</c>
    /// after <paramref name="timeout"/> elapses. Unlike the session's raw
    /// async enumerable, this method sees alerts that arrived BEFORE the
    /// call — including any that fired during fixture ctor or during
    /// earlier test setup.
    /// </summary>
    public async Task<T?> WaitForAsync<T>(
        Func<T, bool> predicate,
        TimeSpan timeout)
        where T : Alert
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var alert in _captured)
            {
                if (alert is T typed && predicate(typed))
                {
                    return typed;
                }
            }
            // 50ms is fast enough to keep test wall-clock low but slow
            // enough not to burn a core on spin-polling; unhappy-path
            // timeouts still resolve in <= 50ms + timeout.
            try
            {
                await Task.Delay(50, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try { _pumpTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        _cts.Dispose();
    }
}
