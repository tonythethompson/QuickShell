using System.Threading;
using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class QuickShellLifetime : IQuickShellLifetime, System.IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken CancellationToken => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public void Cancel() => _cts.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        ProjectClassificationCache.Invalidate();
        GitRepoIndex.Invalidate();
    }
}
