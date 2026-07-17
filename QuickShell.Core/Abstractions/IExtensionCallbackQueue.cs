namespace QuickShell.Abstractions;

/// <summary>
/// Queues UI callbacks for hosts that load on an MTA thread without a
/// <see cref="System.Threading.SynchronizationContext"/>. List pages drain the queue from GetItems.
/// </summary>
internal interface IExtensionCallbackQueue
{
    void Enqueue(Action callback);

    void Drain();
}
