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
        // ETW delivery is host/listener dependent; assert the provider API is callable.
        QuickShellEventSource.Log.WriteRowCache(RowPresentationDiagnostics.CacheHit);
        QuickShellEventSource.Log.WritePlanCache(nameof(LaunchDiagnosticKind.PlanCacheMiss));
        QuickShellEventSource.Log.WriteStartupSpan("test-span", 1.5);
        QuickShellEventSource.Log.WriteRepository("test", "slow-operation", 12);
        QuickShellEventSource.Log.WriteSupportEvent("test.event");
        QuickShellEventSource.Log.WriteSupportWriteFailure(nameof(IOException));
        QuickShellEventSource.Log.WriteGitDiscoveryComplete(3);
        Assert.True(true);
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
}
