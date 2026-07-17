using System.Threading;

namespace QuickShell.Abstractions;

using QuickShell.Services;

internal interface IGitRepoIndex
{
    bool IsRefreshInFlight { get; }

    void Invalidate();

    void Prewarm(IReadOnlyList<string> searchRoots, CancellationToken cancellationToken = default);

    IReadOnlyList<GitRepoCandidate> Search(
        string query,
        IReadOnlyList<string> searchRoots,
        IReadOnlySet<string>? savedDirectories = null,
        int maxResults = 8,
        CancellationToken cancellationToken = default);

    IReadOnlyList<GitRepoCandidate> GetAll(
        IReadOnlyList<string>? extraRoots = null,
        CancellationToken cancellationToken = default);

    void RunAfterNextRefresh(Action callback);

    bool TryRunAfterNextRefreshIfInFlight(Action callback);
}
