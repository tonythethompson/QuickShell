namespace QuickShell.Abstractions;

internal interface IQuickShellLifetime : IDisposable
{
    CancellationToken CancellationToken { get; }

    bool IsCancellationRequested { get; }

    void Cancel();
}
