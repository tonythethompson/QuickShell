using QuickShell.Services;
using System.Diagnostics;

namespace QuickShell.Core.Tests;

[Collection(GitRepoIndexIsolation.Name)]
public sealed class GitRepoIndexCacheTests : IDisposable
{
    public GitRepoIndexCacheTests() => GitRepoIndex.ResetForTests();

    public void Dispose() => GitRepoIndex.ResetForTests();

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
        var result = GitRepoIndex.GetAll([]);
        stopwatch.Stop();

        Assert.Single(result);
        Assert.Equal("Stale", result[0].Name);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Expected non-blocking stale read, took {stopwatch.ElapsedMilliseconds}ms.");

        gate.Set();
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        var refreshed = GitRepoIndex.GetAll([]);
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

        Assert.Empty(GitRepoIndex.GetAll([]));

        gate.Set();
        GitRepoIndex.WaitForRefreshForTests(TimeSpan.FromSeconds(5));
        var refreshed = GitRepoIndex.GetAll([]);
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
        _ = GitRepoIndex.GetAll([]);

        gate.Set();
        Assert.True(
            SpinWait.SpinUntil(() => invoked, TimeSpan.FromSeconds(5)),
            "Expected refresh completion callback to run.");
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

        Assert.Empty(GitRepoIndex.GetAll([@"C:\different-root"]));
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

        var exception = Record.Exception(() => GitRepoIndex.Prewarm([]));
        Assert.Null(exception);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
    }
}
