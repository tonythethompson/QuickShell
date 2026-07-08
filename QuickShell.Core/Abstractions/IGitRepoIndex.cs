namespace QuickShell.Abstractions;

using QuickShell.Services;

internal interface IGitRepoIndex
{
    bool IsRefreshInFlight { get; }

    void Invalidate();

    void Prewarm(IReadOnlyList<string> searchRoots);

    IReadOnlyList<GitRepoCandidate> Search(string query, IReadOnlyList<string> searchRoots);

    IReadOnlyList<GitRepoCandidate> GetAll(IReadOnlyList<string>? extraRoots = null);

    void RunAfterNextRefresh(Action callback);
}
