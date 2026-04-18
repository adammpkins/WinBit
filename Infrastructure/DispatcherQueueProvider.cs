using Microsoft.UI.Dispatching;

namespace WinBit.Infrastructure;

public sealed class DispatcherQueueProvider : IDispatcherQueueProvider
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueProvider(DispatcherQueue queue) => _queue = queue;

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public void Enqueue(Action action) => _queue.TryEnqueue(() => action());

    public Task EnqueueAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource();
        _queue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
