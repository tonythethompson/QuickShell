using System.Collections.Concurrent;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// CmdPal loads the extension on an MTA thread with no <see cref="SynchronizationContext"/>.
/// Background work must queue UI callbacks here; list pages drain the queue from <c>GetItems</c>.
/// </summary>
internal sealed class ExtensionCallbackQueue : IExtensionCallbackQueue
{
    private readonly ConcurrentQueue<Action> _pending = new();

    public void Enqueue(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _pending.Enqueue(callback);
    }

    public void Drain()
    {
        while (_pending.TryDequeue(out var callback))
        {
            try
            {
                callback();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                // Host torn down before callback ran.
            }
            catch (Exception ex)
            {
                // A queued UI callback failed; keep draining so later callbacks still run
                // and page rendering isn't abandoned. Matching RunOnExtensionThread's policy.
                SupportDiagnostics.WriteException("ExtensionCallbackQueue.Drain", ex, hypothesisId: "Q");
            }
        }
    }
}
