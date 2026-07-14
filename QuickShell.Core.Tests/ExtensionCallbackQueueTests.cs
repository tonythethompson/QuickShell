using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// ExtensionCallbackQueue backs the async list-icon upgrade: background work enqueues
/// UI callbacks that list pages drain from GetItems. These tests lock in FIFO ordering
/// and the exception-swallowing contract (a failing callback must not block later ones).
/// </summary>
public sealed class ExtensionCallbackQueueTests
{
    [Fact]
    public void Drain_runs_enqueued_callbacks_in_order()
    {
        var order = new List<int>();
        ExtensionCallbackQueue.Enqueue(() => order.Add(1));
        ExtensionCallbackQueue.Enqueue(() => order.Add(2));
        ExtensionCallbackQueue.Enqueue(() => order.Add(3));

        ExtensionCallbackQueue.Drain();

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void Drain_is_noop_when_empty()
    {
        ExtensionCallbackQueue.Drain();
    }

    [Fact]
    public void Drain_skips_failed_callback_but_runs_the_rest()
    {
        var ran = new List<int>();
        ExtensionCallbackQueue.Enqueue(() => ran.Add(1));
        ExtensionCallbackQueue.Enqueue(() =>
        {
            throw new InvalidOperationException("callback failure must not abort the queue");
        });
        ExtensionCallbackQueue.Enqueue(() => ran.Add(2));

        var exception = Record.Exception(() => ExtensionCallbackQueue.Drain());

        Assert.Null(exception);
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void Drain_continues_past_non_com_exceptions()
    {
        // A non-ObjectDisposed/COM exception (e.g. NullReference) must not abort the
        // drain, mirroring RunOnExtensionThread's swallow-and-continue policy.
        var ran = new List<int>();
        ExtensionCallbackQueue.Enqueue(() => ran.Add(1));
        ExtensionCallbackQueue.Enqueue(() => throw new NullReferenceException());
        ExtensionCallbackQueue.Enqueue(() => ran.Add(2));

        var exception = Record.Exception(() => ExtensionCallbackQueue.Drain());

        Assert.Null(exception);
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void Enqueue_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ExtensionCallbackQueue.Enqueue(null!));
    }

    [Fact]
    public void Drain_only_runs_callbacks_enqueued_before_drain()
    {
        var ran = new List<int>();
        ExtensionCallbackQueue.Enqueue(() => ran.Add(1));
        ExtensionCallbackQueue.Drain();
        ExtensionCallbackQueue.Enqueue(() => ran.Add(2));

        Assert.Equal([1], ran);

        ExtensionCallbackQueue.Drain();
        Assert.Equal([1, 2], ran);
    }
}
