namespace WinBit.Core.Threading;

/// <summary>
/// Marshals an action onto a known synchronization context — typically the WinUI dispatcher
/// thread. Lives in <c>WinBit.Core</c> so engine adapters and other Core services can reach
/// the UI thread without taking a direct dependency on <c>Microsoft.UI.Dispatching</c>. The
/// WinBit app replaces the default <see cref="InlineDispatcherQueueProvider"/> with a real
/// DispatcherQueue-backed implementation in its bootstrap.
/// </summary>
/// <remarks>
/// The namespace is deliberately separate from <c>WinBit.Core.Common</c> to avoid colliding
/// with the legacy <c>WinBit.Infrastructure.IDispatcherQueueProvider</c> used by viewmodels.
/// Phase E consolidates the two; for now they coexist with distinct surfaces (this one is
/// fire-and-forget, the legacy one exposes thread-access checks and async dispatch).
/// </remarks>
public interface IDispatcherQueueProvider
{
    /// <summary>
    /// Posts <paramref name="action"/> to the UI thread (or runs it inline when no dispatcher
    /// is available). Implementations must not throw; failures should be logged and swallowed
    /// so a broken UI hop never tears down a background loop.
    /// </summary>
    void Post(Action action);
}

/// <summary>
/// Default fallback used when no UI is attached — runs <see cref="Post"/> synchronously on the
/// caller's thread. Headless contexts (tests, the Web UI process when there is no desktop
/// shell) can rely on this without any extra wiring.
/// </summary>
public sealed class InlineDispatcherQueueProvider : IDispatcherQueueProvider
{
    public void Post(Action action) => action();
}
