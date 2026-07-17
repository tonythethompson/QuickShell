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
        var queue = new ExtensionCallbackQueue();
        var order = new List<int>();
        queue.Enqueue(() => order.Add(1));
        queue.Enqueue(() => order.Add(2));
        queue.Enqueue(() => order.Add(3));

        queue.Drain();

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void Drain_is_noop_when_empty()
    {
        var queue = new ExtensionCallbackQueue();
        queue.Drain();
    }

    [Fact]
    public void Drain_skips_failed_callback_but_runs_the_rest()
    {
        var queue = new ExtensionCallbackQueue();
        var ran = new List<int>();
        queue.Enqueue(() => ran.Add(1));
        queue.Enqueue(() =>
        {
            throw new InvalidOperationException("callback failure must not abort the queue");
        });
        queue.Enqueue(() => ran.Add(2));

        var exception = Record.Exception(() => queue.Drain());

        Assert.Null(exception);
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void Drain_continues_past_non_com_exceptions()
    {
        // A non-ObjectDisposed/COM exception must not abort the
        // drain, mirroring RunOnExtensionThread's swallow-and-continue policy.
        var queue = new ExtensionCallbackQueue();
        var ran = new List<int>();
        queue.Enqueue(() => ran.Add(1));
        queue.Enqueue(() => throw new InvalidOperationException());
        queue.Enqueue(() => ran.Add(2));

        var exception = Record.Exception(() => queue.Drain());

        Assert.Null(exception);
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void Enqueue_null_throws()
    {
        var queue = new ExtensionCallbackQueue();
        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
    }

    [Fact]
    public void Drain_only_runs_callbacks_enqueued_before_drain()
    {
        var queue = new ExtensionCallbackQueue();
        var ran = new List<int>();
        queue.Enqueue(() => ran.Add(1));
        queue.Drain();
        queue.Enqueue(() => ran.Add(2));

        Assert.Equal([1], ran);

        queue.Drain();
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void TwoQueues_DoNotShareState()
    {
        var first = new ExtensionCallbackQueue();
        var second = new ExtensionCallbackQueue();
        var ran = new List<int>();

        first.Enqueue(() => ran.Add(1));
        second.Drain();
        Assert.Empty(ran);

        first.Drain();
        Assert.Equal([1], ran);
    }
}
