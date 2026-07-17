using System.Threading;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Posts work to the CmdPal extension <see cref="SynchronizationContext"/> when available;
/// otherwise enqueues for list pages to drain via <see cref="IExtensionCallbackQueue"/>.
/// </summary>
internal sealed class CmdPalExtensionThreadScheduler : IExtensionThreadScheduler
{
    private readonly SynchronizationContext? _context;
    private readonly IExtensionCallbackQueue _queue;

    public CmdPalExtensionThreadScheduler(
        SynchronizationContext? context,
        IExtensionCallbackQueue queue)
    {
        _context = context;
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (_context is null)
        {
            _queue.Enqueue(callback);
            return;
        }

        if (ReferenceEquals(SynchronizationContext.Current, _context))
        {
            callback();
            return;
        }

        _context.Post(static state =>
        {
            try
            {
                ((Action)state!).Invoke();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException
                                       and not AppDomainUnloadedException
                                       and not BadImageFormatException
                                       and not CannotUnloadAppDomainException
                                       and not ThreadAbortException)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Ignored exception in extension-thread callback: {0}",
                    ex);
            }
        }, callback);
    }
}
