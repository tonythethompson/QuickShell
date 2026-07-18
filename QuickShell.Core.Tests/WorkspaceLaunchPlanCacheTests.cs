using QuickShell.Services;
using System.Collections.Concurrent;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceLaunchPlanCacheTests
{
    private static LaunchPlanCacheKey Key(string workspaceId, long version = 1) =>
        new(workspaceId, version, "settings", "catalog", null, false, false);

    private static ResolvedWorkspaceLaunchPlan PlanFor(LaunchPlanCacheKey key) =>
        new(key.WorkspaceId, key.RepositoryVersion, [], [], []);

    [Fact]
    public void SameKey_BuildsPlanOnce()
    {
        var cache = new WorkspaceLaunchPlanCache();
        var built = 0;

        var key = Key("ws");
        var plan1 = cache.GetOrBuild(key, () => { Interlocked.Increment(ref built); return PlanFor(key); });
        var plan2 = cache.GetOrBuild(key, () => { Interlocked.Increment(ref built); return PlanFor(key); });

        Assert.Same(plan1, plan2);
        Assert.Equal(1, built);
    }

    [Fact]
    public void ConcurrentSameKey_BuildsPlanOnce()
    {
        var cache = new WorkspaceLaunchPlanCache();
        var built = 0;
        var key = Key("ws");

        Parallel.For(0, 20, _ => cache.GetOrBuild(key, () => { Interlocked.Increment(ref built); return PlanFor(key); }));

        Assert.Equal(1, built);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void OlderRepositoryVersion_IsEvicted()
    {
        var cache = new WorkspaceLaunchPlanCache();
        var built = 0;

        var oldKey = Key("ws", 1);
        var newKey = Key("ws", 2);

        cache.GetOrBuild(oldKey, () => { Interlocked.Increment(ref built); return PlanFor(oldKey); });
        cache.GetOrBuild(newKey, () => { Interlocked.Increment(ref built); return PlanFor(newKey); });

        Assert.Equal(2, built);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Bounded_RemovesOldestEntries()
    {
        var cache = new WorkspaceLaunchPlanCache();
        var built = 0;

        for (var i = 0; i < 60; i++)
        {
            var key = Key($"ws-{i}", 1);
            cache.GetOrBuild(key, () => { Interlocked.Increment(ref built); return PlanFor(key); });
        }

        Assert.True(cache.Count <= 50, $"Expected count <= 50, got {cache.Count}");
        Assert.Equal(60, built);
    }
}
