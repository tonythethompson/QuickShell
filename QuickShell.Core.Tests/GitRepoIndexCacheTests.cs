using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;
using System.Diagnostics;

namespace QuickShell.Core.Tests;

[Collection(GitRepoIndexIsolation.Name)]
public sealed class GitRepoIndexCacheTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;

    public GitRepoIndexCacheTests()
    {
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        GitRepoIndex.ResetForTests();
    }

    public void Dispose()
    {
        _provider.Dispose();
        GitRepoIndex.ResetForTests();
    }

    [Fact]
    public void GetAll_ReturnsStaleCache_WithoutBlockingOnRefresh()
    {
        using var gate = new ManualResetEventSlim(false);
        GitRepoIndex.DiscoverOverride = _ =>
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
        };

        GitRepoIndex.SeedCacheForTests(
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
        var result = GitRepoIndex.GetAll(_projectAnalysis, []);
        stopwatch.Stop();

        Assert.Single(result);
        Assert.Equal("Stale", result[0].Name);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Expected non-blocking stale read, took {stopwatch.ElapsedMilliseconds}ms.");

        gate.Set();
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        var refreshed = GitRepoIndex.GetAll(_projectAnalysis, []);
        Assert.Single(refreshed);
        Assert.Equal("Fresh", refreshed[0].Name);
    }

    [Fact]
    public void Invalidate_PreventsServingStaleCacheUntilRefreshCompletes()
    {
        using var gate = new ManualResetEventSlim(false);
        GitRepoIndex.DiscoverOverride = _ =>
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
        };

        GitRepoIndex.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "BeforeInvalidate",
                    Directory = @"C:\before",
                },
            ],
            rootKey: string.Empty,
            refreshedUtc: DateTime.UtcNow);

        GitRepoIndex.Invalidate();

        Assert.Empty(GitRepoIndex.GetAll(_projectAnalysis, []));

        gate.Set();
        GitRepoIndex.WaitForPopulationForTests(string.Empty, TimeSpan.FromSeconds(5));
        var refreshed = GitRepoIndex.GetAll(_projectAnalysis, []);
        Assert.Single(refreshed);
        Assert.Equal("AfterInvalidate", refreshed[0].Name);
    }

    [Fact]
    public void RunAfterNextRefresh_InvokesCallbackWhenRefreshCompletes()
    {
        var invoked = false;
        using var gate = new ManualResetEventSlim(false);
        GitRepoIndex.DiscoverOverride = _ =>
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
        };

        GitRepoIndex.Invalidate();
        GitRepoIndex.RunAfterNextRefresh(() => invoked = true);
        _ = GitRepoIndex.GetAll(_projectAnalysis, []);

        gate.Set();
        Assert.True(
            SpinWait.SpinUntil(() => invoked, TimeSpan.FromSeconds(5)),
            "Expected refresh completion callback to run.");
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.True(invoked);
    }

    [Fact]
    public void RunAfterNextRefresh_InvokesCallbackWhenRefreshFails()
    {
        var invoked = false;
        GitRepoIndex.DiscoverOverride = _ => throw new InvalidOperationException("discovery failed");

        GitRepoIndex.Invalidate();
        GitRepoIndex.RunAfterNextRefresh(() => invoked = true);
        _ = GitRepoIndex.GetAll(_projectAnalysis, []);

        Assert.True(
            SpinWait.SpinUntil(() => invoked, TimeSpan.FromSeconds(5)),
            "Expected refresh completion callback to run after a failed refresh.");
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(5));

        Assert.True(invoked);
    }

    [Fact]
    public void GetAll_ReturnsEmpty_WhenRootKeyDoesNotMatchCachedData()
    {
        GitRepoIndex.SeedCacheForTests(
            [
                new GitRepoCandidate
                {
                    Name = "OtherRoots",
                    Directory = @"C:\other",
                },
            ],
            rootKey: @"C:\other-root",
            refreshedUtc: DateTime.UtcNow);

        Assert.Empty(GitRepoIndex.GetAll(_projectAnalysis, [@"C:\different-root"]));
    }

    [Fact]
    public void GetAll_DoesNotRescanImmediatelyAfterEmptyRefreshResult()
    {
        var refreshCount = 0;
        GitRepoIndex.DiscoverOverride = _ =>
        {
            Interlocked.Increment(ref refreshCount);
            return [];
        };

        GitRepoIndex.Invalidate();
        _ = GitRepoIndex.GetAll(_projectAnalysis, []);
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref refreshCount));

        _ = GitRepoIndex.GetAll(_projectAnalysis, []);
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Volatile.Read(ref refreshCount));
        Assert.Empty(GitRepoIndex.GetAll(_projectAnalysis, []));
    }

    [Fact]
    public void Prewarm_StartsBackgroundRefresh_WithoutThrowing()
    {
        var completed = new ManualResetEventSlim(false);
        GitRepoIndex.DiscoverOverride = _ =>
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
        };

        var exception = Record.Exception(() => GitRepoIndex.Prewarm(_projectAnalysis, []));
        Assert.Null(exception);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
    }
}
