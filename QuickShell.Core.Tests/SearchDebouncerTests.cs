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
        string? applied = null;
        using var debouncer = new SearchDebouncer(query => applied = query, delayMilliseconds: 30);

        debouncer.Schedule("first");
        debouncer.Schedule("second");

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (applied is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.Equal("second", applied);
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
