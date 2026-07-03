namespace QuickShell.Services;

internal static class GitRepoIndex
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly object Sync = new();

    private static IReadOnlyList<GitRepoCandidate> _cache = [];
    private static DateTime _refreshedUtc = DateTime.MinValue;
    private static Task<IReadOnlyList<GitRepoCandidate>>? _refreshInFlight;

    public static IReadOnlyList<GitRepoCandidate> Search(
        string query,
        IEnumerable<string>? extraRoots = null,
        IReadOnlySet<string>? savedDirectories = null,
        int maxResults = 8)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        EnsureFresh(extraRoots);
        savedDirectories ??= EmptySet.Instance;

        return _cache
            .Where(candidate => !savedDirectories.Contains(candidate.Directory))
            .Where(candidate => Matches(candidate, trimmed))
            .Take(maxResults)
            .ToList();
    }

    public static IReadOnlyList<GitRepoCandidate> GetAll(IEnumerable<string>? extraRoots = null)
    {
        EnsureFresh(extraRoots);
        return _cache;
    }

    public static void Invalidate() =>
        WithLock(() => _refreshedUtc = DateTime.MinValue);

    public static bool IsDiscoverQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return query.Trim() switch
        {
            "git" or "repos" or "repo" or "discover" or "discover git" or "git repos" => true,
            _ => false,
        };
    }

    private static bool Matches(GitRepoCandidate candidate, string query) =>
        candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || candidate.Directory.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (candidate.RemoteUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static void EnsureFresh(IEnumerable<string>? extraRoots)
    {
        Task<IReadOnlyList<GitRepoCandidate>> refreshTask;

        lock (Sync)
        {
            if (_cache.Count > 0 && DateTime.UtcNow - _refreshedUtc < CacheLifetime)
            {
                return;
            }

            // Kick the (possibly multi-second) filesystem scan off the lock so
            // Invalidate() and other callers checking freshness never block on the
            // Monitor for the scan's duration; concurrent callers share this one task.
            _refreshInFlight ??= Task.Run(() => GitRepoDiscovery.Discover(extraRoots));
            refreshTask = _refreshInFlight;
        }

        var discovered = refreshTask.GetAwaiter().GetResult();

        lock (Sync)
        {
            if (ReferenceEquals(_refreshInFlight, refreshTask))
            {
                _cache = discovered;
                _refreshedUtc = DateTime.UtcNow;
                _refreshInFlight = null;
            }
        }
    }

    private static void WithLock(Action action)
    {
        lock (Sync)
        {
            action();
        }
    }

    private sealed class EmptySet : IReadOnlySet<string>
    {
        public static EmptySet Instance { get; } = new();

        public int Count => 0;

        public bool Contains(string item) => false;

        public IEnumerator<string> GetEnumerator()
        {
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsProperSubsetOf(IEnumerable<string> other) => false;

        public bool IsProperSupersetOf(IEnumerable<string> other) => false;

        public bool IsSubsetOf(IEnumerable<string> other) => true;

        public bool IsSupersetOf(IEnumerable<string> other) => false;

        public bool Overlaps(IEnumerable<string> other) => false;

        public bool SetEquals(IEnumerable<string> other) => !other.Any();
    }
}
