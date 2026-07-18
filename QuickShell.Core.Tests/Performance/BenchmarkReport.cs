using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickShell.Core.Tests.Performance;

internal sealed record BenchmarkEnvironment(
    string OperatingSystem,
    string ProcessArchitecture,
    int ProcessorCount,
    string Configuration,
    DateTimeOffset CapturedAtUtc);

internal sealed record BenchmarkArtifact(
    BenchmarkEnvironment Environment,
    IReadOnlyList<BenchmarkStats> Results);

/// <summary>
/// Collects benchmark results for one harness run and writes both a JSON artifact
/// (machine-readable) and a Markdown summary (human-readable) to the same directory.
/// Output directory defaults to a repo-relative <c>artifacts/perf</c> folder and can be
/// overridden with the <c>QUICKSHELL_PERF_OUTPUT_DIR</c> environment variable so CI can
/// redirect it without code changes.
/// </summary>
internal sealed class BenchmarkReport
{
    private readonly List<BenchmarkStats> _results = [];

    public IReadOnlyList<BenchmarkStats> Results => _results;

    public void Add(BenchmarkStats stats) => _results.Add(stats);

    public string WriteArtifacts(string? outputDirectory = null)
    {
        var directory = outputDirectory
            ?? Environment.GetEnvironmentVariable("QUICKSHELL_PERF_OUTPUT_DIR")
            ?? ResolveDefaultOutputDirectory();
        Directory.CreateDirectory(directory);

        var artifact = new BenchmarkArtifact(
            new BenchmarkEnvironment(
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                DateTimeOffset.UtcNow),
            _results);

        var jsonPath = Path.Combine(directory, "quickshell-perf-results.json");
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(artifact, BenchmarkJsonContext.Default.BenchmarkArtifact));

        var markdownPath = Path.Combine(directory, "quickshell-perf-results.md");
        File.WriteAllText(markdownPath, BuildMarkdown(artifact));

        return directory;
    }

    private static string ResolveDefaultOutputDirectory()
    {
        // Walk up from the test binary to the repo root (marked by QuickShell.sln) so the
        // artifact lands in a stable, discoverable place regardless of the build output path.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickShell.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(root, "artifacts", "perf");
    }

    private static string BuildMarkdown(BenchmarkArtifact artifact)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# QuickShell performance regression harness");
        sb.AppendLine();
        sb.AppendLine($"Captured: {artifact.Environment.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"OS: {artifact.Environment.OperatingSystem}");
        sb.AppendLine($"Architecture: {artifact.Environment.ProcessArchitecture}, {artifact.Environment.ProcessorCount} logical processors");
        sb.AppendLine($"Configuration: {artifact.Environment.Configuration}");
        sb.AppendLine();
        sb.AppendLine("> Wall-clock numbers are machine dependent — treat them as relative signals for");
        sb.AppendLine("> regression investigation on the same machine, not universal guarantees.");
        sb.AppendLine();

        foreach (var group in artifact.Results.GroupBy(r => r.Category))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Iterations | Median (ms) | P95 (ms) | Min (ms) | Max (ms) | Mean allocated |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var stats in group)
            {
                sb.AppendLine(
                    $"| {stats.Name} | {stats.Iterations} | {Format(stats.MedianMs)} | {Format(stats.P95Ms)} | " +
                    $"{Format(stats.MinMs)} | {Format(stats.MaxMs)} | {FormatBytes(stats.MeanAllocatedBytes)} |");
            }

            var withCounts = group.Where(r => r.OperationCounts is { Count: > 0 }).ToList();
            if (withCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Operation counts:");
                foreach (var stats in withCounts)
                {
                    var counts = string.Join(", ", stats.OperationCounts!.Select(kv => $"{kv.Key}={kv.Value}"));
                    sb.AppendLine($"- **{stats.Name}**: {counts}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatBytes(double bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024 * 1024):0.##} MiB"
            : bytes >= 1024
                ? $"{bytes / 1024:0.##} KiB"
                : $"{bytes:0} B";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkArtifact))]
internal sealed partial class BenchmarkJsonContext : JsonSerializerContext;
