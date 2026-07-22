using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class SearchDebouncerTests
{
    [Fact]
    public void FlushNow_InvokesCallbackWithLatestScheduledQuery()
    {
        string? applied = null;
        using var debouncer = new SearchDebouncer(query => applied = query, delayMilliseconds: 10_000);

        debouncer.Schedule("a");
        debouncer.Schedule("ab");
        debouncer.FlushNow();

        Assert.Equal("ab", applied);
    }

    [Fact]
    public async Task Schedule_AfterDebounce_InvokesLatestQuery()
    {
        // Timer callbacks use the thread pool; CI load can delay them well past the debounce window.
        var tcs = new TaskCompletionSource<string>();
        using var debouncer = new SearchDebouncer(
            query => tcs.SetResult(query),
            delayMilliseconds: 30);

        debouncer.Schedule("first");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completed == tcs.Task, "Debounced callback did not run within 10s.");
        Assert.Equal("first", await tcs.Task);
    }

    [Fact]
    public void FlushNow_WithNoSchedule_InvokesEmptyPending()
    {
        string? applied = null;
        using var debouncer = new SearchDebouncer(query => applied = query, delayMilliseconds: 10_000);

        debouncer.FlushNow();

        Assert.Equal(string.Empty, applied);
    }

    [Fact]
    public void Dispose_PreventsFurtherSchedule()
    {
        using var debouncer = new SearchDebouncer(_ => { }, delayMilliseconds: 10_000);
        debouncer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => debouncer.Schedule("x"));
        Assert.Throws<ObjectDisposedException>(() => debouncer.FlushNow());
    }
}
