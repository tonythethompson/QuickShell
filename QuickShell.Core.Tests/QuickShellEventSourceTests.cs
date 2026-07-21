using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class QuickShellEventSourceTests
{
    [Fact]
    public void RowPresentationDiagnostics_Record_Forwards_To_EventSource()
    {
        var events = new RecordingQuickShellEventSource();
        var diagnostics = new RowPresentationDiagnostics(events);

        diagnostics.Record(RowPresentationDiagnostics.CacheHit);
        diagnostics.Record(RowPresentationDiagnostics.CacheMiss);

        Assert.Equal(
            [RowPresentationDiagnostics.CacheHit, RowPresentationDiagnostics.CacheMiss],
            events.RowCacheKinds);
        Assert.Equal(1, diagnostics.GetCount(RowPresentationDiagnostics.CacheHit));
        Assert.Equal(1, diagnostics.GetCount(RowPresentationDiagnostics.CacheMiss));
    }

    [Fact]
    public void QuickShellEventSource_WriteMethods_DoNotThrow()
    {
        using var listener = new TestEventListener();
        listener.EnableEvents(QuickShellEventSource.Log, System.Diagnostics.Tracing.EventLevel.Informational);

        QuickShellEventSource.Log.WriteRowCache(RowPresentationDiagnostics.CacheHit);
        QuickShellEventSource.Log.WritePlanCache(nameof(LaunchDiagnosticKind.PlanCacheMiss));
        QuickShellEventSource.Log.WriteStartupSpan("test-span", 1.5);
        QuickShellEventSource.Log.WriteRepository("test", "slow-operation", 12);
        QuickShellEventSource.Log.WriteSupportEvent("test.event");
        QuickShellEventSource.Log.WriteSupportWriteFailure(nameof(IOException));
        QuickShellEventSource.Log.WriteGitDiscoveryComplete(3);

        // Verify all seven events were emitted with expected IDs.
        Assert.Contains(listener.Events, e => e.EventId == 1 && e.EventName == "RowCache");
        Assert.Contains(listener.Events, e => e.EventId == 2 && e.EventName == "PlanCache");
        Assert.Contains(listener.Events, e => e.EventId == 3 && e.EventName == "StartupSpan");
        Assert.Contains(listener.Events, e => e.EventId == 4 && e.EventName == "Repository");
        Assert.Contains(listener.Events, e => e.EventId == 5 && e.EventName == "SupportEvent");
        Assert.Contains(listener.Events, e => e.EventId == 6 && e.EventName == "SupportWriteFailure");
        Assert.Contains(listener.Events, e => e.EventId == 7 && e.EventName == "GitDiscoveryComplete");

        // Verify payload shapes for a sample event.
        var rowCacheEvent = listener.Events.First(e => e.EventId == 1);
        Assert.Single(rowCacheEvent.Payload);
        Assert.Equal(RowPresentationDiagnostics.CacheHit, rowCacheEvent.Payload[0]);

        var startupSpanEvent = listener.Events.First(e => e.EventId == 3);
        Assert.Equal(2, startupSpanEvent.Payload.Count);
        Assert.Equal("test-span", startupSpanEvent.Payload[0]);
        Assert.Equal(1.5, startupSpanEvent.Payload[1]);
    }

    [Fact]
    public void QuickShellEventSource_WriteRepository_NullElapsedMs_RecordsSentinel()
    {
        using var listener = new TestEventListener();
        listener.EnableEvents(QuickShellEventSource.Log, System.Diagnostics.Tracing.EventLevel.Informational);

        var correlation = Guid.NewGuid().ToString("N");
        QuickShellEventSource.Log.WriteRepository(correlation, "no-duration");

        var repositoryEvent = listener.Events.First(e =>
            e.EventId == 4 && e.EventName == "Repository" && Equals(e.Payload.ElementAtOrDefault(0), correlation));
        Assert.Equal(3, repositoryEvent.Payload.Count);
        Assert.Equal(-1L, repositoryEvent.Payload[2]);
    }

    private sealed class RecordingQuickShellEventSource : IQuickShellEventSource
    {
        public List<string> RowCacheKinds { get; } = [];

        public bool IsEnabled() => true;

        public void WriteRowCache(string kind) => RowCacheKinds.Add(kind);

        public void WritePlanCache(string kind)
        {
        }

        public void WriteStartupSpan(string name, double elapsedMs)
        {
        }

        public void WriteRepository(string location, string eventName, long? elapsedMs = null)
        {
        }

        public void WriteSupportEvent(string eventCode)
        {
        }

        public void WriteSupportWriteFailure(string exceptionType)
        {
        }

        public void WriteGitDiscoveryComplete(int repoCount)
        {
        }
    }

    private sealed class TestEventListener : System.Diagnostics.Tracing.EventListener
    {
        public System.Collections.Concurrent.ConcurrentQueue<EventData> Events { get; } = new();

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs eventData)
        {
            Events.Enqueue(new EventData(eventData.EventId, eventData.EventName ?? string.Empty, eventData.Payload?.ToList() ?? []));
        }

        public sealed record EventData(int EventId, string EventName, List<object?> Payload);
    }
}
