using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using QuickShell.Classification;

namespace QuickShell.Services;

internal static class ProjectClassificationCache
{
    private const int MaxEntries = 64;

    private static readonly ConcurrentDictionary<string, CacheEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> InsertionOrder = new();
    private static readonly object EvictionLock = new();

    public static ProjectClassification Classify(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return ProjectClassification.Empty;
        }

        var normalized = directory.Trim();
        var fingerprint = BuildFingerprint(normalized);
        if (Entries.TryGetValue(normalized, out var cached)
            && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return cached.Classification;
        }

        var classification = ProjectAnalysisAccessor.Instance.Classify(normalized);
        Entries[normalized] = new CacheEntry(fingerprint, classification);
        TrackInsertion(normalized);
        return classification;
    }

    public static void Invalidate(string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            lock (EvictionLock)
            {
                Entries.Clear();
                InsertionOrder.Clear();
            }

            return;
        }

        var normalized = directory.Trim();
        Entries.TryRemove(normalized, out _);
        lock (EvictionLock)
        {
            RemoveFromQueue(normalized);
        }
    }

    private static void TrackInsertion(string normalized)
    {
        lock (EvictionLock)
        {
            RemoveFromQueue(normalized);
            InsertionOrder.Enqueue(normalized);
            while (Entries.Count > MaxEntries && InsertionOrder.TryDequeue(out var oldest))
            {
                Entries.TryRemove(oldest, out _);
            }
        }
    }

    private static void RemoveFromQueue(string normalized)
    {
        if (InsertionOrder.Count == 0)
        {
            return;
        }

        var retained = new Queue<string>(InsertionOrder.Count);
        while (InsertionOrder.TryDequeue(out var entry))
        {
            if (!string.Equals(entry, normalized, StringComparison.OrdinalIgnoreCase))
            {
                retained.Enqueue(entry);
            }
        }

        while (retained.TryDequeue(out var entry))
        {
            InsertionOrder.Enqueue(entry);
        }
    }

    private static readonly string[] ClassifierMarkerFiles =
    [
        "package.json",
        "pnpm-workspace.yaml",
        "pnpm-workspace.yml",
        "bun.lockb",
        "bun.lock",
        "Cargo.toml",
        "pyproject.toml",
        "requirements.txt",
        "setup.py",
        "docker-compose.yml",
        "docker-compose.yaml",
        "compose.yml",
        "compose.yaml",
        "Makefile",
        "makefile",
        "justfile",
        "Justfile",
        "Taskfile.yml",
        "Taskfile.yaml",
        "go.mod",
        "deno.json",
        "deno.jsonc",
        "Procfile",
        "Gemfile",
        "mix.exs",
        "pom.xml",
        "build.gradle",
        "build.gradle.kts",
        "devcontainer.json",
        Path.Combine(".vscode", "tasks.json"),
    ];

    private static string BuildFingerprint(string directory)
    {
        var builder = new StringBuilder();
        foreach (var marker in ClassifierMarkerFiles)
        {
            AppendFileFingerprint(builder, Path.Combine(directory, marker));
        }

        AppendDirectoryFingerprint(builder, Path.Combine(directory, ".vscode"));
        AppendDirectoryFingerprint(builder, Path.Combine(directory, ".devcontainer"));

        foreach (var workspace in Directory
                     .EnumerateFiles(directory, "*.code-workspace", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            AppendFileFingerprint(builder, workspace);
        }

        foreach (var project in Directory
                     .EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                     .Where(path =>
                         path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .Take(CommandSuggestionService.MaxRootProjects))
        {
            AppendFileFingerprint(builder, project);
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void AppendFileFingerprint(StringBuilder builder, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        builder.Append(path);
        builder.Append('|');
        builder.Append(info.Length);
        builder.Append('|');
        builder.Append(info.LastWriteTimeUtc.Ticks);
        builder.Append(';');
    }

    private static void AppendDirectoryFingerprint(StringBuilder builder, string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var info = new DirectoryInfo(path);
        builder.Append(path);
        builder.Append('|');
        builder.Append(info.LastWriteTimeUtc.Ticks);
        builder.Append(';');
    }

    private sealed record CacheEntry(string Fingerprint, ProjectClassification Classification);
}
