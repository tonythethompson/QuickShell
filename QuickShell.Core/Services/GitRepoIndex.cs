namespace QuickShell.Services;

internal static class GitRepoIndex
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly object Sync = new();

    private static IReadOnlyList<GitRepoCandidate> _cache = [];
    private static string _cacheRootKey = string.Empty;
    private static DateTime _refreshedUtc = DateTime.MinValue;
    private static RefreshInFlight? _refreshInFlight;
    private static readonly List<Action> RefreshCompletedHandlers = [];
    private static readonly object RefreshHandlerSync = new();

    internal static Func<IReadOnlyList<string>, IReadOnlyList<GitRepoCandidate>>? DiscoverOverride { get; set; }

    public static bool IsRefreshInFlight
    {
        get
        {
            lock (Sync)
            {
                return _refreshInFlight is not null;
            }
        }
    }

    public static void RunAfterNextRefresh(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (RefreshHandlerSync)
        {
            RefreshCompletedHandlers.Add(callback);
        }
    }

    public static bool TryRunAfterNextRefreshIfInFlight(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (Sync)
        {
            if (_refreshInFlight is null)
            {
                return false;
            }

            lock (RefreshHandlerSync)
            {
                RefreshCompletedHandlers.Add(callback);
            }

            return true;
        }
    }

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

        var rootKey = BuildRootKey(SnapshotRoots(extraRoots));
        EnsureFresh(extraRoots);
        savedDirectories ??= EmptySet.Instance;

        return GetCacheForRootKey(rootKey)
            .Where(candidate => !savedDirectories.Contains(candidate.Directory))
            .Where(candidate => Matches(candidate, trimmed))
            .Take(maxResults)
            .ToList();
    }

    public static IReadOnlyList<GitRepoCandidate> GetAll(IEnumerable<string>? extraRoots = null)
    {
        var rootKey = BuildRootKey(SnapshotRoots(extraRoots));
        EnsureFresh(extraRoots);
        return GetCacheForRootKey(rootKey);
    }

    public static void Prewarm(IEnumerable<string>? extraRoots = null) => EnsureFresh(extraRoots);

    public static void Invalidate() =>
        WithLock(() =>
        {
            _cache = [];
            _refreshedUtc = DateTime.MinValue;
            _cacheRootKey = string.Empty;
            _refreshInFlight = null;
        });

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

    internal static void WaitForRefreshForTests(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Task? pending;
            lock (Sync)
            {
                pending = _refreshInFlight?.Task;
                if (pending is null)
                {
                    return;
                }
            }

            if (pending.Wait(TimeSpan.FromMilliseconds(50)))
            {
                return;
            }
        }

        throw new TimeoutException("GitRepoIndex refresh did not complete.");
    }

    internal static void WaitForPopulationForTests(string rootKey, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Task? pending;
            lock (Sync)
            {
                if (string.Equals(_cacheRootKey, rootKey, StringComparison.Ordinal) && _cache.Count > 0)
                {
                    return;
                }

                pending = _refreshInFlight?.Task;
            }

            if (pending is not null)
            {
                pending.Wait(TimeSpan.FromMilliseconds(50));
            }
            else
            {
                Thread.Sleep(10);
            }
        }

        throw new TimeoutException($"GitRepoIndex did not populate cache for root key '{rootKey}'.");
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            _cache = [];
            _cacheRootKey = string.Empty;
            _refreshedUtc = DateTime.MinValue;
            _refreshInFlight = null;
            DiscoverOverride = null;
            lock (RefreshHandlerSync)
            {
                RefreshCompletedHandlers.Clear();
            }
        }
    }

    internal static void SeedCacheForTests(
        IReadOnlyList<GitRepoCandidate> cache,
        string rootKey,
        DateTime refreshedUtc)
    {
        lock (Sync)
        {
            _cache = cache;
            _cacheRootKey = rootKey;
            _refreshedUtc = refreshedUtc;
            _refreshInFlight = null;
        }
    }

    private static IReadOnlyList<GitRepoCandidate> GetCacheForRootKey(string rootKey)
    {
        lock (Sync)
        {
            return string.Equals(_cacheRootKey, rootKey, StringComparison.Ordinal) ? _cache : [];
        }
    }

    private static bool Matches(GitRepoCandidate candidate, string query) =>
        candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || candidate.Directory.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (candidate.RemoteUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || candidate.Classification.Labels.Any(label => label.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static void EnsureFresh(IEnumerable<string>? extraRoots)
    {
        var rootSnapshot = SnapshotRoots(extraRoots);
        var rootKey = BuildRootKey(rootSnapshot);

        lock (Sync)
        {
            if (IsCacheFreshLocked(rootKey))
            {
                return;
            }

            StartRefreshLocked(rootKey, rootSnapshot);
        }
    }

    private static bool IsCacheFreshLocked(string rootKey) =>
        _cache.Count > 0
        && string.Equals(_cacheRootKey, rootKey, StringComparison.Ordinal)
        && DateTime.UtcNow - _refreshedUtc < CacheLifetime;

    private static void StartRefreshLocked(string rootKey, string[] rootSnapshot)
    {
        if (_refreshInFlight is not null
            && string.Equals(_refreshInFlight.RootKey, rootKey, StringComparison.Ordinal))
        {
            return;
        }

        var inFlight = new RefreshInFlight(
            rootKey,
            Task.Run(() => DiscoverForRefresh(rootSnapshot)));

        _ = inFlight.Task.ContinueWith(
            task => CompleteRefresh(inFlight, task),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _refreshInFlight = inFlight;
    }

    private static IReadOnlyList<GitRepoCandidate> DiscoverForRefresh(IReadOnlyList<string> rootSnapshot) =>
        DiscoverOverride?.Invoke(rootSnapshot) ?? GitRepoDiscovery.Discover(rootSnapshot);

    private static void CompleteRefresh(RefreshInFlight inFlight, Task<IReadOnlyList<GitRepoCandidate>> task)
    {
        var shouldNotify = false;
        lock (Sync)
        {
            if (!ReferenceEquals(_refreshInFlight, inFlight))
            {
                return;
            }

            _refreshInFlight = null;
            shouldNotify = true;

            if (task.IsFaulted || task.IsCanceled)
            {
                return;
            }

            _cache = task.Result;
            _cacheRootKey = inFlight.RootKey;
            _refreshedUtc = DateTime.UtcNow;
        }

        if (shouldNotify)
        {
            NotifyRefreshCompleted();
        }
    }

    private static void NotifyRefreshCompleted()
    {
        Action[] handlers;
        lock (RefreshHandlerSync)
        {
            if (RefreshCompletedHandlers.Count == 0)
            {
                return;
            }

            handlers = RefreshCompletedHandlers.ToArray();
            RefreshCompletedHandlers.Clear();
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException
                                       and not AppDomainUnloadedException
                                       and not BadImageFormatException
                                       and not CannotUnloadAppDomainException
                                       and not System.Threading.ThreadAbortException)
            {
                // Best effort; UI callbacks should not break cache refresh.
            }
        }
    }

    private static string[] SnapshotRoots(IEnumerable<string>? extraRoots) =>
        extraRoots?
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private static string BuildRootKey(IEnumerable<string> roots) =>
        string.Join('\n', roots);

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

    private sealed record RefreshInFlight(string RootKey, Task<IReadOnlyList<GitRepoCandidate>> Task);
}
