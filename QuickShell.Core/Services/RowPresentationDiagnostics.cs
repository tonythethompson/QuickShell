using System.Collections.Concurrent;

namespace QuickShell.Services;

/// <summary>
/// Named counters for row presentation and enrichment instrumentation. Counter-based so
/// tests can assert cache/enrichment behavior deterministically without log parsing.
/// </summary>
internal static class RowPresentationDiagnostics
{
    public const string CacheHit = "row-cache:hit";
    public const string CacheMiss = "row-cache:miss";
    public const string CacheBuild = "row-cache:build";
    public const string EnrichmentQueued = "row-enrichment:queued";
    public const string EnrichmentBatchApplied = "row-enrichment:batch-applied";
    public const string EnrichmentDiscardedStale = "row-enrichment:discarded-stale";
    public const string EnrichmentCancelled = "row-enrichment:cancelled";

    private static readonly ConcurrentDictionary<string, long> Counters = new(StringComparer.Ordinal);

    public static void Record(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        Counters.AddOrUpdate(eventName, 1, static (_, count) => count + 1);
    }

    public static long GetCount(string eventName) =>
        Counters.TryGetValue(eventName, out var count) ? count : 0;

    public static void ResetForTests() => Counters.Clear();
}
