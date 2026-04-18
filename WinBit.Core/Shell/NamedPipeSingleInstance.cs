using System.IO.Pipes;
using System.Text;

namespace WinBit.Core.Shell;

/// <summary>
/// Single-instance enforcement backed by a named <see cref="Mutex"/> and a named pipe channel.
/// The first process to call <see cref="TryAcquirePrimary"/> becomes the primary, starts the
/// pipe server, and handles activations. Subsequent launches forward their command line to the
/// primary via <see cref="ForwardAsync"/> and exit.
/// </summary>
public sealed class NamedPipeSingleInstance : IDisposable
{
    private readonly string _instanceName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public NamedPipeSingleInstance(string instanceName)
    {
        _instanceName = instanceName;
        _pipeName = instanceName + ".pipe";
    }

    public bool IsPrimary { get; private set; }

    /// <summary>
    /// Attempts to become the primary instance by acquiring a Local\ named mutex. Returns
    /// <c>true</c> when this process won the race, <c>false</c> when another instance already
    /// holds it.
    /// </summary>
    public bool TryAcquirePrimary()
    {
        // The createdNew out flag is the definitive signal: exactly one handle opener per named
        // mutex is told "you created it". Subsequent handles to the same named object get false.
        // We deliberately do not call WaitOne here — that's re-entrant on the owning thread and
        // skews in-process tests. Ownership for release isn't needed because the kernel object
        // goes away when the last handle is closed.
        _mutex = new Mutex(initiallyOwned: false, name: @"Local\" + _instanceName, out var createdNew);
        IsPrimary = createdNew;
        return IsPrimary;
    }

    /// <summary>
    /// Forwards a command-line string (no exe path; the activation payload, joined with spaces)
    /// to the running primary. Returns <c>false</c> if no primary is reachable within
    /// <paramref name="timeout"/>.
    /// </summary>
    public async Task<bool> ForwardAsync(string commandLine, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(commandLine + '\n');
            await client.WriteAsync(bytes, ct).ConfigureAwait(false);
            await client.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts a background loop that accepts connections on the named pipe and feeds received
    /// command-line strings into <paramref name="onReceive"/>. Must only be called on the
    /// primary instance. Stops when <see cref="Dispose"/> is called.
    /// </summary>
    public void StartListening(Action<string> onReceive)
    {
        if (!IsPrimary)
        {
            throw new InvalidOperationException("StartListening is only valid on the primary instance.");
        }
        if (_listenerTask is not null)
        {
            return;
        }
        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(onReceive, _listenerCts.Token));
    }

    private async Task ListenLoopAsync(Action<string> onReceive, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch (IOException)
            {
                // Pipe name collision — bail; next launch will behave as primary.
                return;
            }

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (IOException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    onReceive(line);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A broken connection shouldn't take down the listener.
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _listenerCts?.Cancel();
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore — disposal is best-effort.
        }
        _listenerCts?.Dispose();

        if (_mutex is not null)
        {
            // We never acquired with WaitOne, so there's nothing to release — just close the
            // handle and let the kernel drop the named object when the last handle goes.
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
