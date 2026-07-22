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
    public void Schedule_AfterDebounce_InvokesLatestQuery()
    {
        // Timer callbacks use the thread pool; CI load can delay them well past the debounce window.
        using var applied = new ManualResetEventSlim(false);
        string? value = null;
        using var debouncer = new SearchDebouncer(
            query =>
            {
                value = query;
                applied.Set();
            },
            delayMilliseconds: 30);

        debouncer.Schedule("first");
        debouncer.Schedule("second");

        Assert.True(applied.Wait(TimeSpan.FromSeconds(10)), "Debounced callback did not run within 10s.");
        Assert.Equal("second", value);
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
