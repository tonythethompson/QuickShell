using System.Collections.Concurrent;
using System.Text;

using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal sealed class ProjectClassificationCache : IProjectClassificationCache
{
    private const int MaxEntries = 64;

    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _insertionOrder = new();
    private readonly object _evictionLock = new();

    public ProjectClassificationCache(IProjectAnalysisService projectAnalysis)
    {
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
    }

    public ProjectClassification Classify(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return ProjectClassification.Empty;
        }

        var normalized = directory.Trim();
        // Cheap signature every call (markers + typed globs). Full project analysis only
        // when the signature changes — avoids SHA256 + top-level *.* walks on the hot path.
        var signature = BuildCheapSignature(normalized);
        if (_entries.TryGetValue(normalized, out var cached)
            && string.Equals(cached.Signature, signature, StringComparison.Ordinal))
        {
            return cached.Classification;
        }

        var classification = _projectAnalysis.Classify(normalized);
        _entries[normalized] = new CacheEntry(signature, classification);
        TrackInsertion(normalized);
        return classification;
    }

    public void Invalidate(string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            lock (_evictionLock)
            {
                _entries.Clear();
                _insertionOrder.Clear();
            }

            return;
        }

        var normalized = directory.Trim();
        _entries.TryRemove(normalized, out _);
        lock (_evictionLock)
        {
            RemoveFromQueue(normalized);
        }
    }

    private void TrackInsertion(string normalized)
    {
        lock (_evictionLock)
        {
            RemoveFromQueue(normalized);
            _insertionOrder.Enqueue(normalized);
            while (_entries.Count > MaxEntries && _insertionOrder.TryDequeue(out var oldest))
            {
                _entries.TryRemove(oldest, out _);
            }
        }
    }

    private void RemoveFromQueue(string normalized)
    {
        if (_insertionOrder.Count == 0)
        {
            return;
        }

        var retained = new Queue<string>(_insertionOrder.Count);
        while (_insertionOrder.TryDequeue(out var entry))
        {
            if (!string.Equals(entry, normalized, StringComparison.OrdinalIgnoreCase))
            {
                retained.Enqueue(entry);
            }
        }

        while (retained.TryDequeue(out var entry))
        {
            _insertionOrder.Enqueue(entry);
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
        // Nested markers: directory mtimes alone miss in-place edits on Windows.
        Path.Combine(".vscode", "tasks.json"),
        Path.Combine(".devcontainer", "devcontainer.json"),
    ];

    /// <summary>
    /// Nested folders whose contents feed classification (VS Code tasks, dev containers).
    /// We stamp files inside them — parent directory LastWriteTime often does not change
    /// when a child file is edited in place.
    /// </summary>
    private static readonly string[] NestedContentDirectories =
    [
        ".vscode",
        ".devcontainer",
    ];

    private const int MaxNestedFilesPerDirectory = 16;

    private static readonly string[] RootProjectGlobPatterns =
    [
        "*.csproj",
        "*.fsproj",
        "*.sln",
        "*.slnx",
        "*.code-workspace",
    ];

    /// <summary>
    /// Lightweight directory signature for cache invalidation. Avoids SHA256 and avoids a
    /// full top-level <c>*.*</c> walk; only known markers + nested content dirs + typed globs.
    /// </summary>
    internal static string BuildCheapSignature(string directory)
    {
        var builder = new StringBuilder(512);
        AppendDirectoryStamp(builder, directory);

        foreach (var marker in ClassifierMarkerFiles)
        {
            AppendFileStamp(builder, Path.Combine(directory, marker), relativeLabel: marker);
        }

        foreach (var nested in NestedContentDirectories)
        {
            AppendNestedDirectorySignature(builder, directory, nested);
        }

        foreach (var pattern in RootProjectGlobPatterns)
        {
            IEnumerable<string> matches;
            try
            {
                matches = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var path in matches
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                AppendFileStamp(builder, path);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Stamp a nested folder and a bounded set of its top-level files so edits to
    /// <c>tasks.json</c> / <c>devcontainer.json</c> (and siblings) invalidate the cache.
    /// </summary>
    private static void AppendNestedDirectorySignature(
        StringBuilder builder,
        string rootDirectory,
        string relativeDirectory)
    {
        var nestedPath = Path.Combine(rootDirectory, relativeDirectory);
        try
        {
            if (!Directory.Exists(nestedPath))
            {
                return;
            }

            AppendDirectoryStamp(builder, nestedPath);

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(nestedPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return;
            }

            foreach (var path in files
                         .OrderBy(static f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                         .Take(MaxNestedFilesPerDirectory))
            {
                var label = Path.Combine(relativeDirectory, Path.GetFileName(path));
                AppendFileStamp(builder, path, relativeLabel: label);
            }
        }
        catch
        {
            // Ignore inaccessible nested directories.
        }
    }

    private static void AppendFileStamp(StringBuilder builder, string path, string? relativeLabel = null)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            builder.Append('f');
            builder.Append(relativeLabel ?? info.Name);
            builder.Append('|');
            builder.Append(info.Length);
            builder.Append('|');
            builder.Append(info.LastWriteTimeUtc.Ticks);
            builder.Append(';');
        }
        catch
        {
            // Ignore inaccessible files.
        }
    }

    private static void AppendDirectoryStamp(StringBuilder builder, string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists)
            {
                return;
            }

            builder.Append('d');
            builder.Append(info.Name);
            builder.Append('|');
            builder.Append(info.LastWriteTimeUtc.Ticks);
            builder.Append(';');
        }
        catch
        {
            // Ignore inaccessible directories.
        }
    }

    private sealed record CacheEntry(string Signature, ProjectClassification Classification);
}
