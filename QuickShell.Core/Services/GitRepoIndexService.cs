using System.Threading;
using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class GitRepoIndexService : IGitRepoIndex
{
    public bool IsRefreshInFlight => GitRepoIndex.IsRefreshInFlight;

    public void Invalidate() => GitRepoIndex.Invalidate();

    public void Prewarm(IReadOnlyList<string> searchRoots, CancellationToken cancellationToken = default) =>
        GitRepoIndex.Prewarm(searchRoots, cancellationToken);

    public IReadOnlyList<GitRepoCandidate> Search(string query, IReadOnlyList<string> searchRoots, CancellationToken cancellationToken = default) =>
        GitRepoIndex.Search(query, searchRoots, cancellationToken: cancellationToken);

    public IReadOnlyList<GitRepoCandidate> GetAll(IReadOnlyList<string>? extraRoots = null, CancellationToken cancellationToken = default) =>
        GitRepoIndex.GetAll(extraRoots, cancellationToken);

    public void RunAfterNextRefresh(Action callback) => GitRepoIndex.RunAfterNextRefresh(callback);
}
