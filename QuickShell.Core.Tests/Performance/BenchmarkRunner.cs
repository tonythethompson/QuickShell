using System.Diagnostics;

namespace QuickShell.Core.Tests.Performance;

/// <summary>
/// One measured scenario: wall-clock distribution, allocations, and optional operation
/// counts captured around the measured action. Wall-clock numbers are machine dependent
/// and informational only — nothing in the harness asserts on them.
/// </summary>
internal sealed record BenchmarkStats(
    string Name,
    string Category,
    int Iterations,
    double MedianMs,
    double P95Ms,
    double MinMs,
    double MaxMs,
    double MeanAllocatedBytes,
    IReadOnlyDictionary<string, long>? OperationCounts,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
/// Runs a scenario with one untimed warm-up iteration (JIT/first-call costs excluded) followed
/// by N timed samples, reporting median/p95/min/max wall-clock and mean allocated bytes.
/// </summary>
internal static class BenchmarkRunner
{
    public static BenchmarkStats Measure(
        string name,
        string category,
        Action action,
        int iterations = 5,
        IReadOnlyDictionary<string, string>? metadata = null,
        Func<IReadOnlyDictionary<string, long>>? countersAfter = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        // Untimed warm-up: JIT, first-call caching, etc. must not skew the samples.
        action();

        var samplesMs = new double[iterations];
        var allocatedBytes = new long[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            allocatedBytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            samplesMs[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samplesMs);
        var operationCounts = countersAfter?.Invoke();

        return new BenchmarkStats(
            name,
            category,
            iterations,
            Median(samplesMs),
            Percentile(samplesMs, 0.95),
            samplesMs[0],
            samplesMs[^1],
            allocatedBytes.Average(),
            operationCounts,
            metadata);
    }

    /// <summary>For scenarios where "cold" and "warm" must be captured as two distinct
    /// single-shot measurements (e.g. the first call populates a cache the rest would hit).</summary>
    public static BenchmarkStats MeasureOnce(
        string name,
        string category,
        Action action,
        IReadOnlyDictionary<string, string>? metadata = null,
        Func<IReadOnlyDictionary<string, long>>? countersAfter = null)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = stopwatch.Elapsed.TotalMilliseconds;

        return new BenchmarkStats(
            name,
            category,
            1,
            ms,
            ms,
            ms,
            ms,
            allocated,
            countersAfter?.Invoke(),
            metadata);
    }

    private static double Median(double[] sorted)
    {
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var rank = percentile * (sorted.Length - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        var fraction = rank - lowerIndex;
        return sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * fraction);
    }
}
