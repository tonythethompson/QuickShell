using System.Collections.Concurrent;
using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Named counters for row presentation and enrichment instrumentation. Counter-based so
/// tests can assert cache/enrichment behavior deterministically without log parsing.
/// Instance-scoped (registered as a DI singleton): production shares one instance for the
/// process lifetime, while tests construct their own instance per test for isolation
/// instead of resetting shared static state.
/// </summary>
internal sealed class RowPresentationDiagnostics : IRowPresentationDiagnostics
{
    public const string CacheHit = "row-cache:hit";
    public const string CacheMiss = "row-cache:miss";
    public const string CacheBuild = "row-cache:build";
    public const string EnrichmentQueued = "row-enrichment:queued";
    public const string EnrichmentBatchApplied = "row-enrichment:batch-applied";
    public const string EnrichmentDiscardedStale = "row-enrichment:discarded-stale";
    public const string EnrichmentCancelled = "row-enrichment:cancelled";

    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly IQuickShellEventSource _events;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowPresentationDiagnostics"/> class.
    /// </summary>
    /// <param name="events">The event source used to emit row-cache diagnostics.</param>
    public RowPresentationDiagnostics(IQuickShellEventSource? events = null)
    {
        _events = events ?? QuickShellEventSource.Log;
    }

    /// <summary>
    /// Records a row presentation diagnostic event and emits it to the configured event source.
    /// </summary>
    /// <param name="eventName">The name of the diagnostic event to record.</param>
    public void Record(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        _counters.AddOrUpdate(eventName, 1, static (_, count) => count + 1);
        _events.WriteRowCache(eventName);
    }

    /// <summary>
        /// Retrieves the recorded count for an event name.
        /// </summary>
        /// <param name="eventName">The name of the event to count.</param>
        /// <returns>The recorded count, or zero if the event has not been recorded.</returns>
        public long GetCount(string eventName) =>
        _counters.TryGetValue(eventName, out var count) ? count : 0;
}
