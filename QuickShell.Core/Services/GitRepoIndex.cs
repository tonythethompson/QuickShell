using System.Threading;

using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal sealed class GitRepoIndex : IGitRepoIndex, IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly object _refreshHandlerSync = new();
    private readonly List<Action> _refreshCompletedHandlers = [];
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly IQuickShellLifetime _lifetime;
    private readonly IExtensionThreadScheduler _threadScheduler;
    private readonly Func<IReadOnlyList<string>, bool, IReadOnlyList<GitRepoCandidate>>? _discoverOverride;

    private IReadOnlyList<GitRepoCandidate> _cache = [];
    private string _cacheRootKey = string.Empty;
    private bool _cacheIncludesDefaultSearchRoots;
    private DateTime _refreshedUtc = DateTime.MinValue;
    private bool _hasCompletedRefreshForRoot;
    private RefreshInFlight? _refreshInFlight;
    private bool _disposed;

    public GitRepoIndex(
        IProjectAnalysisService projectAnalysis,
        IQuickShellLifetime lifetime,
        IExtensionThreadScheduler threadScheduler)
        : this(
            projectAnalysis,
            lifetime,
            threadScheduler,
            (Func<IReadOnlyList<string>, IReadOnlyList<GitRepoCandidate>>?)null)
    {
    }

    /// <summary>Test constructor that injects a discover override without static seams.</summary>
    internal GitRepoIndex(
        IProjectAnalysisService projectAnalysis,
        IQuickShellLifetime lifetime,
        IExtensionThreadScheduler threadScheduler,
        Func<IReadOnlyList<string>, IReadOnlyList<GitRepoCandidate>>? discoverOverride)
        : this(
            projectAnalysis,
            lifetime,
            threadScheduler,
            discoverOverride is null ? null : (roots, _) => discoverOverride(roots),
            scopedDiscoverOverride: true)
    {
    }

    /// <summary>Test constructor that observes discovery scope without static seams.</summary>
    internal GitRepoIndex(
        IProjectAnalysisService projectAnalysis,
        IQuickShellLifetime lifetime,
        IExtensionThreadScheduler threadScheduler,
        Func<IReadOnlyList<string>, bool, IReadOnlyList<GitRepoCandidate>> discoverOverride)
        : this(projectAnalysis, lifetime, threadScheduler, discoverOverride, scopedDiscoverOverride: true)
    {
    }

    private GitRepoIndex(
        IProjectAnalysisService projectAnalysis,
        IQuickShellLifetime lifetime,
        IExtensionThreadScheduler threadScheduler,
        Func<IReadOnlyList<string>, bool, IReadOnlyList<GitRepoCandidate>>? discoverOverride,
        bool scopedDiscoverOverride)
    {
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _threadScheduler = threadScheduler ?? throw new ArgumentNullException(nameof(threadScheduler));
        _discoverOverride = discoverOverride;
    }

    public bool IsRefreshInFlight
    {
        get
        {
            lock (_sync)
            {
                return _refreshInFlight is not null;
            }
        }
    }

    public void RunAfterNextRefresh(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();

        lock (_refreshHandlerSync)
        {
            _refreshCompletedHandlers.Add(callback);
        }
    }

    public bool TryRunAfterNextRefreshIfInFlight(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();

        lock (_sync)
        {
            if (_refreshInFlight is null)
            {
                return false;
            }

            lock (_refreshHandlerSync)
            {
                _refreshCompletedHandlers.Add(callback);
            }

            return true;
        }
    }

    public IReadOnlyList<GitRepoCandidate> Search(
        string query,
        IReadOnlyList<string> searchRoots,
        IReadOnlySet<string>? savedDirectories = null,
        int maxResults = 8,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        var rootKey = BuildRootKey(SnapshotRoots(searchRoots));
        EnsureFresh(searchRoots, includeDefaultSearchRoots: true, cancellationToken: cancellationToken);
        savedDirectories ??= EmptySet.Instance;

        // Single linear pass with early exit — index size is bounded by discovery, not workspaces.
        maxResults = Math.Max(0, maxResults);
        if (maxResults == 0)
        {
            return [];
        }

        var cache = GetCacheForRootKey(rootKey, includeDefaultSearchRoots: true);
        List<GitRepoCandidate>? results = null;
        foreach (var candidate in cache)
        {
            if (savedDirectories.Contains(candidate.Directory))
            {
                continue;
            }

            if (!Matches(candidate, trimmed))
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

        return results is null ? [] : results;
    }

    public IReadOnlyList<GitRepoCandidate> GetAll(
        IReadOnlyList<string>? extraRoots = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var rootKey = BuildRootKey(SnapshotRoots(extraRoots));
        EnsureFresh(extraRoots, includeDefaultSearchRoots: true, cancellationToken: cancellationToken);
        return GetCacheForRootKey(rootKey, includeDefaultSearchRoots: true);
    }

    public void Prewarm(
        IReadOnlyList<string> searchRoots,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // Prewarm only scans roots derived from the supplied workspace snapshot.
        // Default drive scanning is left to on-demand Search/GetAll so activation
        // does not enumerate the filesystem broadly.
        EnsureFresh(searchRoots, includeDefaultSearchRoots: false, cancellationToken: cancellationToken);
    }

    public void Invalidate()
    {
        ThrowIfDisposed();
        WithLock(() =>
        {
            _cache = [];
            _refreshedUtc = DateTime.MinValue;
            _cacheRootKey = string.Empty;
            _cacheIncludesDefaultSearchRoots = false;
            _hasCompletedRefreshForRoot = false;
            _refreshInFlight = null;
        });
    }

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

    internal void WaitForRefreshForTests(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Task? pending;
            lock (_sync)
            {
                pending = _refreshInFlight?.Task;
                if (pending is null)
                {
                    return;
                }
            }

            // pending is the raw discovery Task, not its CompleteRefresh continuation (which
            // clears _refreshInFlight under _sync once the cache is actually updated). That
            // continuation runs with ExecuteSynchronously as a hint only, so it can still land
            // on a different thread moments later — waiting on `pending` and returning as soon
            // as it completes can race ahead of the cache write. Loop back to _refreshInFlight
            // itself so this only returns once the continuation has actually run.
            pending.Wait(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("GitRepoIndex refresh did not complete.");
    }

    internal void WaitForPopulationForTests(string rootKey, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Task? pending;
            lock (_sync)
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

    internal void SeedCacheForTests(
        IReadOnlyList<GitRepoCandidate> cache,
        string rootKey,
        DateTime refreshedUtc,
        bool includeDefaultSearchRoots = true)
    {
        lock (_sync)
        {
            _cache = cache;
            _cacheRootKey = rootKey;
            _cacheIncludesDefaultSearchRoots = includeDefaultSearchRoots;
            _refreshedUtc = refreshedUtc;
            _hasCompletedRefreshForRoot = true;
            _refreshInFlight = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        RefreshInFlight? inFlight;
        lock (_sync)
        {
            inFlight = _refreshInFlight;
            _refreshInFlight = null;
            _cache = [];
            _cacheRootKey = string.Empty;
            _cacheIncludesDefaultSearchRoots = false;
            _refreshedUtc = DateTime.MinValue;
            _hasCompletedRefreshForRoot = false;
        }

        if (inFlight is not null)
        {
            try
            {
                inFlight.LinkedCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Another thread may have already disposed the linked CTS during teardown.
                // Safe to ignore here because Dispose() is best-effort cleanup.
            }

            DisposeLinkedCts(inFlight);
        }

        lock (_refreshHandlerSync)
        {
            _refreshCompletedHandlers.Clear();
        }
    }

    private static void DisposeLinkedCts(RefreshInFlight inFlight)
    {
        try
        {
            inFlight.LinkedCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private IReadOnlyList<GitRepoCandidate> GetCacheForRootKey(
        string rootKey,
        bool includeDefaultSearchRoots)
    {
        lock (_sync)
        {
            return IsCacheScopeCompatibleLocked(rootKey, includeDefaultSearchRoots) ? _cache : [];
        }
    }

    internal static bool Matches(GitRepoCandidate candidate, string query) =>
        candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || candidate.Directory.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (candidate.RemoteUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || candidate.Classification.Labels.Any(label => label.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void EnsureFresh(
        IEnumerable<string>? extraRoots,
        bool includeDefaultSearchRoots = true,
        CancellationToken cancellationToken = default)
    {
        var rootSnapshot = SnapshotRoots(extraRoots);
        var rootKey = BuildRootKey(rootSnapshot);

        lock (_sync)
        {
            if (IsCacheFreshLocked(rootKey, includeDefaultSearchRoots))
            {
                return;
            }

            StartRefreshLocked(rootKey, rootSnapshot, includeDefaultSearchRoots, cancellationToken);
        }
    }

    private bool IsCacheFreshLocked(string rootKey, bool includeDefaultSearchRoots) =>
        _hasCompletedRefreshForRoot
        && IsCacheScopeCompatibleLocked(rootKey, includeDefaultSearchRoots)
        && DateTime.UtcNow - _refreshedUtc < CacheLifetime;

    private bool IsCacheScopeCompatibleLocked(string rootKey, bool includeDefaultSearchRoots) =>
        string.Equals(_cacheRootKey, rootKey, StringComparison.Ordinal)
        && (_cacheIncludesDefaultSearchRoots || !includeDefaultSearchRoots);

    private void StartRefreshLocked(
        string rootKey,
        string[] rootSnapshot,
        bool includeDefaultSearchRoots,
        CancellationToken cancellationToken)
    {
        if (_refreshInFlight is not null
            && string.Equals(_refreshInFlight.RootKey, rootKey, StringComparison.Ordinal)
            && (_refreshInFlight.IncludesDefaultSearchRoots || !includeDefaultSearchRoots))
        {
            return;
        }

        CancellationTokenSource linkedCts;
        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.CancellationToken,
                cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            // Lifetime already disposed — skip starting a new refresh.
            return;
        }

        var tokenForTask = linkedCts.Token;
        var inFlight = new RefreshInFlight(
            rootKey,
            includeDefaultSearchRoots,
            Task.Run(() => DiscoverForRefresh(rootSnapshot, includeDefaultSearchRoots, tokenForTask), tokenForTask),
            linkedCts);

        _refreshInFlight = inFlight;
        _ = inFlight.Task.ContinueWith(
            task => CompleteRefresh(inFlight, task, tokenForTask),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private IReadOnlyList<GitRepoCandidate> DiscoverForRefresh(
        IReadOnlyList<string> rootSnapshot,
        bool includeDefaultSearchRoots,
        CancellationToken cancellationToken) =>
        _discoverOverride?.Invoke(rootSnapshot, includeDefaultSearchRoots)
        ?? GitRepoDiscovery.Discover(_projectAnalysis, rootSnapshot, includeDefaultSearchRoots: includeDefaultSearchRoots, cancellationToken: cancellationToken);

    private void CompleteRefresh(
        RefreshInFlight inFlight,
        Task<IReadOnlyList<GitRepoCandidate>> task,
        CancellationToken cancellationToken)
    {
        var shouldNotify = false;
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_refreshInFlight, inFlight))
            {
                DisposeLinkedCts(inFlight);
                return;
            }

            shouldNotify = true;

            if (!task.IsFaulted && !task.IsCanceled && !cancellationToken.IsCancellationRequested)
            {
                _cache = task.Result;
                _cacheRootKey = inFlight.RootKey;
                _cacheIncludesDefaultSearchRoots = inFlight.IncludesDefaultSearchRoots;
                _refreshedUtc = DateTime.UtcNow;
                _hasCompletedRefreshForRoot = true;
            }

            _refreshInFlight = null;
            DisposeLinkedCts(inFlight);
        }

        if (shouldNotify)
        {
            NotifyRefreshCompleted();
        }
    }

    private void NotifyRefreshCompleted()
    {
        Action[] handlers;
        lock (_refreshHandlerSync)
        {
            if (_refreshCompletedHandlers.Count == 0)
            {
                return;
            }

            handlers = _refreshCompletedHandlers.ToArray();
            _refreshCompletedHandlers.Clear();
        }

        foreach (var handler in handlers)
        {
            try
            {
                _threadScheduler.Post(handler);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException
                                       and not AppDomainUnloadedException
                                       and not BadImageFormatException
                                       and not CannotUnloadAppDomainException
                                       and not ThreadAbortException)
            {
                // Best effort; UI callbacks should not break cache refresh.
            }
        }
    }

    internal static string[] SnapshotRoots(IEnumerable<string>? extraRoots) =>
        extraRoots?
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    internal static string BuildRootKey(IEnumerable<string> roots) =>
        string.Join('\n', roots);

    private void WithLock(Action action)
    {
        lock (_sync)
        {
            action();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

    private sealed record RefreshInFlight(
        string RootKey,
        bool IncludesDefaultSearchRoots,
        Task<IReadOnlyList<GitRepoCandidate>> Task,
        CancellationTokenSource LinkedCts);
}
