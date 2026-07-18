using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;
using System.Diagnostics;

namespace QuickShell.Core.Tests;

public sealed class GitRepoIndexCacheTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly IQuickShellLifetime _lifetime;
    private readonly IExtensionThreadScheduler _scheduler;

    public GitRepoIndexCacheTests()
    {
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _lifetime = _provider.GetRequiredService<IQuickShellLifetime>();
        _scheduler = new SyncExtensionThreadScheduler();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private GitRepoIndex CreateIndex(
        Func<IReadOnlyList<string>, IReadOnlyList<GitRepoCandidate>>? discoverOverride = null) =>
        new(_projectAnalysis, _lifetime, _scheduler, discoverOverride);

    [Fact]
    public void GetAll_ReturnsStaleCache_WithoutBlockingOnRefresh()
    {
        using var gate = new ManualResetEventSlim(false);
        using var index = CreateIndex(_ =>
        {
            gate.Wait();
            return
            [
                new GitRepoCandidate
                {
                    Name = "Fresh",
                    Directory = @"C:\fresh",
                },
            ];
        });

        index.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "Stale",
                    Directory = @"C:\stale",
                },
            ],
            rootKey: string.Empty,
            refreshedUtc: DateTime.UtcNow.AddMinutes(-20));

        var stopwatch = Stopwatch.StartNew();
        var result = index.GetAll([]);
        stopwatch.Stop();

        Assert.Single(result);
        Assert.Equal("Stale", result[0].Name);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Expected non-blocking stale read, took {stopwatch.ElapsedMilliseconds}ms.");

        gate.Set();
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        var refreshed = index.GetAll([]);
        Assert.Single(refreshed);
        Assert.Equal("Fresh", refreshed[0].Name);
    }

    [Fact]
    public void Invalidate_PreventsServingStaleCacheUntilRefreshCompletes()
    {
        using var gate = new ManualResetEventSlim(false);
        using var index = CreateIndex(_ =>
        {
            gate.Wait();
            return
            [
                new GitRepoCandidate
                {
                    Name = "AfterInvalidate",
                    Directory = @"C:\after",
                },
            ];
        });

        index.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "BeforeInvalidate",
                    Directory = @"C:\before",
                },
            ],
            rootKey: string.Empty,
            refreshedUtc: DateTime.UtcNow);

        index.Invalidate();

        Assert.Empty(index.GetAll([]));

        gate.Set();
        index.WaitForPopulationForTests(string.Empty, TimeSpan.FromSeconds(5));
        var refreshed = index.GetAll([]);
        Assert.Single(refreshed);
        Assert.Equal("AfterInvalidate", refreshed[0].Name);
    }

    [Fact]
    public void RunAfterNextRefresh_InvokesCallbackWhenRefreshCompletes()
    {
        var invoked = false;
        using var gate = new ManualResetEventSlim(false);
        using var index = CreateIndex(_ =>
        {
            gate.Wait();
            return
            [
                new GitRepoCandidate
                {
                    Name = "AfterCallback",
                    Directory = @"C:\after-callback",
                },
            ];
        });

        index.Invalidate();
        index.RunAfterNextRefresh(() => invoked = true);
        _ = index.GetAll([]);

        gate.Set();
        Assert.True(
            SpinWait.SpinUntil(() => invoked, TimeSpan.FromSeconds(5)),
            "Expected refresh completion callback to run.");
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.True(invoked);
    }

    [Fact]
    public void RunAfterNextRefresh_InvokesCallbackWhenRefreshFails()
    {
        var invoked = false;
        using var index = CreateIndex(_ => throw new InvalidOperationException("discovery failed"));

        index.Invalidate();
        index.RunAfterNextRefresh(() => invoked = true);
        _ = index.GetAll([]);

        Assert.True(
            SpinWait.SpinUntil(() => invoked, TimeSpan.FromSeconds(5)),
            "Expected refresh completion callback to run after a failed refresh.");
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.True(invoked);
    }

    [Fact]
    public void GetAll_ReturnsEmpty_WhenRootKeyDoesNotMatchCachedData()
    {
        using var index = CreateIndex();
        index.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "OtherRoots",
                    Directory = @"C:\other",
                },
            ],
            rootKey: @"C:\other-root",
            refreshedUtc: DateTime.UtcNow);

        Assert.Empty(index.GetAll([@"C:\different-root"]));
    }

    [Fact]
    public void GetAll_DoesNotRescanImmediatelyAfterEmptyRefreshResult()
    {
        var refreshCount = 0;
        using var index = CreateIndex(_ =>
        {
            Interlocked.Increment(ref refreshCount);
            return [];
        });

        index.Invalidate();
        _ = index.GetAll([]);
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref refreshCount));

        _ = index.GetAll([]);
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Volatile.Read(ref refreshCount));
        Assert.Empty(index.GetAll([]));
    }

    [Fact]
    public void Prewarm_StartsBackgroundRefresh_WithoutThrowing()
    {
        var completed = new ManualResetEventSlim(false);
        using var index = CreateIndex(_ =>
        {
            completed.Set();
            return
            [
                new GitRepoCandidate
                {
                    Name = "Prewarmed",
                    Directory = @"C:\prewarmed",
                },
            ];
        });

        var exception = Record.Exception(() => index.Prewarm([]));
        Assert.Null(exception);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void GetAll_AfterCompletedRestrictedPrewarm_StartsFullDiscovery()
    {
        var scopes = new List<bool>();
        using var fullStarted = new ManualResetEventSlim(false);
        using var releaseFull = new ManualResetEventSlim(false);
        using var index = new GitRepoIndex(
            _projectAnalysis,
            _lifetime,
            _scheduler,
            (_, includeDefaultSearchRoots) =>
            {
                lock (scopes)
                {
                    scopes.Add(includeDefaultSearchRoots);
                }

                if (includeDefaultSearchRoots)
                {
                    fullStarted.Set();
                    releaseFull.Wait(TimeSpan.FromSeconds(5));
                }

                return includeDefaultSearchRoots
                    ?
                    [
                        new GitRepoCandidate { Name = "Saved", Directory = @"C:\saved" },
                        new GitRepoCandidate { Name = "Default", Directory = @"D:\default" },
                    ]
                    :
                    [
                        new GitRepoCandidate { Name = "Saved", Directory = @"C:\saved" },
                    ];
            });

        index.Prewarm([@"C:\saved"]);
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.Empty(index.GetAll([@"C:\saved"]));
        Assert.True(fullStarted.Wait(TimeSpan.FromSeconds(5)));
        releaseFull.Set();
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.Equal(2, index.GetAll([@"C:\saved"]).Count);
        Assert.Equal([false, true], scopes);
    }

    [Fact]
    public void GetAll_DuringRestrictedPrewarm_StartsIndependentFullDiscovery()
    {
        using var restrictedStarted = new ManualResetEventSlim(false);
        using var releaseRestricted = new ManualResetEventSlim(false);
        using var fullStarted = new ManualResetEventSlim(false);
        using var releaseFull = new ManualResetEventSlim(false);
        using var index = new GitRepoIndex(
            _projectAnalysis,
            _lifetime,
            _scheduler,
            (_, includeDefaultSearchRoots) =>
            {
                if (!includeDefaultSearchRoots)
                {
                    restrictedStarted.Set();
                    releaseRestricted.Wait(TimeSpan.FromSeconds(5));
                    return [new GitRepoCandidate { Name = "Saved", Directory = @"C:\saved" }];
                }

                fullStarted.Set();
                releaseFull.Wait(TimeSpan.FromSeconds(5));
                return
                [
                    new GitRepoCandidate { Name = "Saved", Directory = @"C:\saved" },
                    new GitRepoCandidate { Name = "Default", Directory = @"D:\default" },
                ];
            });

        index.Prewarm([@"C:\saved"]);
        Assert.True(restrictedStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.Empty(index.GetAll([@"C:\saved"]));
        Assert.True(fullStarted.Wait(TimeSpan.FromSeconds(5)));
        releaseFull.Set();
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        releaseRestricted.Set();

        Assert.Equal(2, index.GetAll([@"C:\saved"]).Count);
    }

    [Fact]
    public void Prewarm_DuringFullDiscovery_DoesNotSupersedeFullRefresh()
    {
        using var fullStarted = new ManualResetEventSlim(false);
        using var releaseFull = new ManualResetEventSlim(false);
        var discoveryCount = 0;
        using var index = new GitRepoIndex(
            _projectAnalysis,
            _lifetime,
            _scheduler,
            (_, includeDefaultSearchRoots) =>
            {
                Interlocked.Increment(ref discoveryCount);
                Assert.True(includeDefaultSearchRoots);
                fullStarted.Set();
                releaseFull.Wait(TimeSpan.FromSeconds(5));
                return
                [
                    new GitRepoCandidate { Name = "Saved", Directory = @"C:\saved" },
                    new GitRepoCandidate { Name = "Default", Directory = @"D:\default" },
                ];
            });

        Assert.Empty(index.GetAll([@"C:\saved"]));
        Assert.True(fullStarted.Wait(TimeSpan.FromSeconds(5)));

        index.Prewarm([@"C:\saved"]);
        Assert.Equal(1, Volatile.Read(ref discoveryCount));
        releaseFull.Set();
        index.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.Equal(2, index.GetAll([@"C:\saved"]).Count);
        Assert.Equal(1, Volatile.Read(ref discoveryCount));
    }

    [Fact]
    public void TwoServiceProviders_DoNotShareCacheState()
    {
        using var providerA = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        using var providerB = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();

        var indexA = (GitRepoIndex)providerA.GetRequiredService<IGitRepoIndex>();
        var indexB = (GitRepoIndex)providerB.GetRequiredService<IGitRepoIndex>();

        indexA.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "OnlyA",
                    Directory = @"C:\only-a",
                },
            ],
            rootKey: string.Empty,
            refreshedUtc: DateTime.UtcNow);

        Assert.Single(indexA.GetAll([]));
        Assert.Empty(indexB.GetAll([]));
        Assert.NotSame(indexA, indexB);
    }

    [Fact]
    public void Prewarm_ScopedCache_DoesNotSatisfy_OnDemand_IncludeDefaultSearchRoots_Request()
    {
        // A Prewarm that scanned only saved workspace roots must not satisfy a later
        // Search/GetAll request that needs the broader default-drive scan, otherwise
        // repos outside the saved roots would disappear from Discover until the cache
        // expires or is invalidated.
        using var index = CreateIndex();

        index.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "SavedOnly",
                    Directory = @"C:\saved-only",
                },
            ],
            rootKey: string.Empty,
            refreshedUtc: DateTime.UtcNow,
            includeDefaultSearchRoots: false);

        Assert.Empty(index.GetAll([]));
        Assert.Empty(index.Search("anything", []));
    }

    [Fact]
    public void FullDiscoveryCache_Satisfies_Narrower_And_Equal_Scoped_Requests()
    {
        using var index = CreateIndex();

        index.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "Discovered",
                    Directory = @"C:\discovered",
                },
            ],
            rootKey: string.Empty,
            refreshedUtc: DateTime.UtcNow,
            includeDefaultSearchRoots: true);

        Assert.Single(index.GetAll([]));
        Assert.Single(index.Search("disc", []));
    }
}
