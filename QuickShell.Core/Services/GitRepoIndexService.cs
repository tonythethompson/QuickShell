using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class GitRepoIndexService : IGitRepoIndex
{
    public bool IsRefreshInFlight => GitRepoIndex.IsRefreshInFlight;

    public void Invalidate() => GitRepoIndex.Invalidate();

    public void Prewarm(IReadOnlyList<string> searchRoots) => GitRepoIndex.Prewarm(searchRoots);

    public IReadOnlyList<GitRepoCandidate> Search(string query, IReadOnlyList<string> searchRoots) =>
        GitRepoIndex.Search(query, searchRoots);

    public IReadOnlyList<GitRepoCandidate> GetAll(IReadOnlyList<string>? extraRoots = null) =>
        GitRepoIndex.GetAll(extraRoots);

    public void RunAfterNextRefresh(Action callback) => GitRepoIndex.RunAfterNextRefresh(callback);
}
