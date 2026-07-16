using System.Threading;

namespace QuickShell.Abstractions;

internal interface IQuickShellLifetime
{
    CancellationToken CancellationToken { get; }

    bool IsCancellationRequested { get; }

    void Cancel();
}
