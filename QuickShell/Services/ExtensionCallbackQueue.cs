using System.Collections.Concurrent;

namespace QuickShell.Services;

/// <summary>
/// CmdPal loads the extension on an MTA thread with no <see cref="SynchronizationContext"/>.
/// Background work must queue UI callbacks here; list pages drain the queue from <c>GetItems</c>.
/// </summary>
internal static class ExtensionCallbackQueue
{
    private static readonly ConcurrentQueue<Action> Pending = new();

    internal static void Enqueue(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Pending.Enqueue(callback);
    }

    internal static void Drain()
    {
        while (Pending.TryDequeue(out var callback))
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
