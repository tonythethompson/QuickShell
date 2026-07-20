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

    public void Record(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        _counters.AddOrUpdate(eventName, 1, static (_, count) => count + 1);
    }

    public long GetCount(string eventName) =>
        _counters.TryGetValue(eventName, out var count) ? count : 0;
}
