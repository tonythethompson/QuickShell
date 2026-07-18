using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

internal sealed class FakeGitRepoIndex : IGitRepoIndex
{
    public IReadOnlyList<GitRepoCandidate> Repos { get; set; } = [];

    public bool IsRefreshInFlight => false;

    public void Invalidate()
    {
    }

    public void Prewarm(IReadOnlyList<string> searchRoots, CancellationToken cancellationToken = default)
    {
    }

    public IReadOnlyList<GitRepoCandidate> Search(
        string query,
        IReadOnlyList<string> searchRoots,
        IReadOnlySet<string>? savedDirectories = null,
        int maxResults = 8,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        savedDirectories ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<GitRepoCandidate>? results = null;
        foreach (var candidate in Repos)
        {
            if (savedDirectories.Contains(candidate.Directory))
            {
                continue;
            }

            if (!candidate.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) &&
                !candidate.Directory.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results ??= new List<GitRepoCandidate>(Math.Min(maxResults, 8));
            results.Add(candidate);
            if (results.Count >= maxResults)
            {
                break;
            }
        }

        return results ?? [];
    }

    public IReadOnlyList<GitRepoCandidate> GetAll(
        IReadOnlyList<string>? extraRoots = null,
        CancellationToken cancellationToken = default)
    {
        return Repos;
    }

    public void RunAfterNextRefresh(Action callback)
    {
    }

    public bool TryRunAfterNextRefreshIfInFlight(Action callback) => false;
}
