using System.Collections.Concurrent;

namespace QuickShell.Services;

/// <summary>
/// Bounded, revision-keyed cache for immutable <see cref="ResolvedWorkspaceLaunchPlan"/> values.
/// Concurrent requests for the same key are single-flighted through a lazy factory.
/// </summary>
internal sealed class WorkspaceLaunchPlanCache
{
    private const int MaxEntries = 50;

    private readonly ConcurrentDictionary<LaunchPlanCacheKey, CacheEntry> _entries = new();
    private long _maxRepositoryVersion;

    public ResolvedWorkspaceLaunchPlan GetOrBuild(
        LaunchPlanCacheKey key,
        Func<ResolvedWorkspaceLaunchPlan> buildPlan,
        Action? onHit = null,
        Action? onMiss = null,
        Action? onBuild = null,
        Action? onEvicted = null)
    {
        if (key.RepositoryVersion > _maxRepositoryVersion)
        {
            EvictOlderVersions(key.RepositoryVersion, onEvicted);
        }

        var entry = _entries.GetOrAdd(
            key,
            _ => new CacheEntry(
                new Lazy<ResolvedWorkspaceLaunchPlan>(
                    () =>
                    {
                        onBuild?.Invoke();
                        return buildPlan();
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication),
                DateTimeOffset.UtcNow));

        if (entry.Plan.IsValueCreated)
        {
            onHit?.Invoke();
        }
        else
        {
            onMiss?.Invoke();
        }

        var plan = entry.Plan.Value;

        TrimToCapacity(onEvicted);

        return plan;
    }

    /// <summary>Number of cached plans. Exposed for test assertions.</summary>
    internal int Count => _entries.Count;

    private void EvictOlderVersions(long newVersion, Action? onEvicted)
    {
        var previous = Interlocked.Exchange(ref _maxRepositoryVersion, newVersion);
        if (previous >= newVersion)
        {
            return;
        }

        var removedAny = false;
        foreach (var (k, _) in _entries.ToArray())
        {
            if (k.RepositoryVersion < newVersion && _entries.TryRemove(k, out _))
            {
                removedAny = true;
            }
        }

        if (removedAny)
        {
            onEvicted?.Invoke();
        }
    }

    private void TrimToCapacity(Action? onEvicted)
    {
        while (_entries.Count > MaxEntries)
        {
            var oldest = _entries.MinBy(static e => e.Value.CreatedAt).Key;
            if (oldest.WorkspaceId is null)
            {
                break;
            }

            if (_entries.TryRemove(oldest, out _))
            {
                onEvicted?.Invoke();
            }
        }
    }

    private sealed record CacheEntry(
        Lazy<ResolvedWorkspaceLaunchPlan> Plan,
        DateTimeOffset CreatedAt);
}
