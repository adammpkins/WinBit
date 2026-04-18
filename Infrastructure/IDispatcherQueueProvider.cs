namespace WinBit.Infrastructure;

/// <summary>
/// Abstracts the UI dispatcher so WinBit.Core code can marshal onto the UI thread without taking
/// a dependency on Microsoft.UI.Dispatching.
/// </summary>
public interface IDispatcherQueueProvider
{
    bool HasThreadAccess { get; }
    void Enqueue(Action action);
    Task EnqueueAsync(Func<Task> action);
}
