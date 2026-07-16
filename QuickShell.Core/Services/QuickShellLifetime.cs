using System.Threading;
using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class QuickShellLifetime : IQuickShellLifetime
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public CancellationToken CancellationToken => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public void Cancel()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }

        ProjectClassificationCache.Invalidate();
        GitRepoIndex.Invalidate();
    }
}
