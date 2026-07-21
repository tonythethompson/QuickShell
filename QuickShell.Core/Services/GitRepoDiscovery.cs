using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;

namespace QuickShell.Services;

internal sealed class GitRepoCandidate
{
    public string Directory { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? RemoteUrl { get; init; }

    public ProjectClassification Classification { get; init; } = ProjectClassification.Empty;
}

internal static partial class GitRepoDiscovery
{
    private const int MaxRepos = 50;
    private const int MaxDirectoriesScanned = 2000;
    private const int MaxDepth = 5;
    private const int DefaultMaxDegreeOfParallelism = 4;

    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build",
        "out",
        "target",
        "AppData",
        "Program Files",
        "Program Files (x86)",
        "Windows",
        ".nuget",
        ".vscode",
        ".cursor",
    };

    private static readonly string[] CommonRootFolderNames =
    [
        "Projects",
        "projects",
        "dev",
        "Development",
        "code",
        "repos",
        "source",
        "src",
        "Documents",
    ];

    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    private sealed record Scope(bool IncludeDefaultSearchRoots, IReadOnlyList<string>? DefaultRootCandidates);

    /// <summary>
    /// Test-only scoped override for the default (no-explicit-root) search behavior. Unlike a
    /// shared mutable static, <see cref="AsyncLocal{T}"/> flows only through the execution
    /// context that set it (including its own child <c>Task.Run</c> calls), so it never leaks
    /// into unrelated concurrently-running tests. Needed because the CmdPal host entry point
    /// (<c>QuickShellCommandsProvider</c>'s public parameterless constructor) has no seam for
    /// injecting a discover scope, yet its background git prewarm must not scan the real
    /// machine during tests.
    /// </summary>
    internal sealed class TestScope : IDisposable
    {
        private readonly Scope? _previous;

        public TestScope(bool includeDefaultSearchRoots, IReadOnlyList<string>? defaultRootCandidates)
        {
            _previous = CurrentScope.Value;
            CurrentScope.Value = new Scope(includeDefaultSearchRoots, defaultRootCandidates);
        }

        public void Dispose() => CurrentScope.Value = _previous;
    }

    public static IReadOnlyList<GitRepoCandidate> Discover(
        IProjectAnalysisService projectAnalysis,
        IEnumerable<string>? extraRoots = null,
        int maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism,
        bool includeDefaultSearchRoots = true,
        IReadOnlyList<string>? defaultRootCandidates = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectAnalysis);
        var scope = CurrentScope.Value;
        if (scope is not null)
        {
            includeDefaultSearchRoots = scope.IncludeDefaultSearchRoots;
            defaultRootCandidates = scope.DefaultRootCandidates;
        }

        var roots = BuildSearchRoots(extraRoots, includeDefaultSearchRoots, defaultRootCandidates);
        if (cancellationToken.IsCancellationRequested)
        {
            return [];
        }

        var roots = BuildSearchRoots(extraRoots, includeDefaultSearchRoots, defaultRootCandidates);

        if (roots.Count == 0)
        {
            QuickShellEventSource.Log.WriteGitDiscoveryComplete(0);
            return [];
        }

        var results = new List<GitRepoCandidate>();
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new ConcurrentQueue<ScanWorkItem>();
        using var signal = new SemaphoreSlim(0);
        var sync = new object();
        var scanned = 0;
        var pending = 0;
        var workerCount = Math.Clamp(maxDegreeOfParallelism, 1, Environment.ProcessorCount);

        foreach (var root in roots)
        {
            Enqueue(root, depth: 0);
        }

        var workers = Enumerable
            .Range(0, workerCount)
            .Select(_ => Task.Run(Worker, cancellationToken))
            .ToArray();

        Task.WaitAll(workers, CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();

        var ordered = results
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        QuickShellEventSource.Log.WriteGitDiscoveryComplete(ordered.Count);
        return ordered;

        void Enqueue(string directory, int depth)
        {
            Interlocked.Increment(ref pending);

            if (ShouldStop())
            {
                if (Interlocked.Decrement(ref pending) == 0)
                {
                    for (var i = 0; i < workerCount; i++)
                    {
                        signal.Release();
                    }
                }

                return;
            }

            queue.Enqueue(new ScanWorkItem(directory, depth));
            signal.Release();
        }

        void Worker()
        {
            while (true)
            {
                try
                {
                    signal.Wait(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!queue.TryDequeue(out var workItem))
                {
                    if (Volatile.Read(ref pending) == 0)
                    {
                        return;
                    }

                    continue;
                }

                try
                {
                    ScanDirectory(workItem);
                }
                finally
                {
                    if (Interlocked.Decrement(ref pending) == 0)
                    {
                        for (var i = 0; i < workerCount; i++)
                        {
                            signal.Release();
                        }
                    }
                }
            }
        }

        void ScanDirectory(ScanWorkItem workItem)
        {
            if (workItem.Depth > MaxDepth || ShouldStop())
            {
                return;
            }

            if (!Directory.Exists(workItem.Directory))
            {
                return;
            }

            if (IsGitRepository(workItem.Directory))
            {
                var candidate = new GitRepoCandidate
                {
                    Directory = workItem.Directory,
                    Name = Path.GetFileName(workItem.Directory.TrimEnd('\\', '/')),
                    RemoteUrl = TryReadOriginRemoteUrl(workItem.Directory),
                    Classification = projectAnalysis.Classify(workItem.Directory),
                };

                lock (sync)
                {
                    if (results.Count < MaxRepos && seenDirectories.Add(workItem.Directory))
                    {
                        results.Add(candidate);
                    }
                }

                return;
            }

            lock (sync)
            {
                if (results.Count >= MaxRepos || scanned >= MaxDirectoriesScanned)
                {
                    return;
                }

                scanned++;
            }

            foreach (var child in GetChildDirectories(workItem.Directory))
            {
                if (ShouldStop())
                {
                    break;
                }

                var name = Path.GetFileName(child);
                if (string.IsNullOrWhiteSpace(name) || SkipDirectoryNames.Contains(name))
                {
                    continue;
                }

                Enqueue(child, workItem.Depth + 1);
            }
        }

        bool ShouldStop()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return true;
            }

            lock (sync)
            {
                return results.Count >= MaxRepos || scanned >= MaxDirectoriesScanned;
            }
        }
    }

    private static List<string> BuildSearchRoots(
        IEnumerable<string>? extraRoots,
        bool includeDefaultSearchRoots,
        IReadOnlyList<string>? defaultRootCandidates)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void AddRoot(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            if (WorkspacePath.TryNormalizeLexical(candidate, out var normalized, out _)
                && Directory.Exists(normalized)
                && seen.Add(normalized))
            {
                roots.Add(normalized);
            }
        }

        if (extraRoots is not null)
        {
            foreach (var extraRoot in extraRoots)
            {
                AddRoot(extraRoot);
            }
        }

        if (includeDefaultSearchRoots && !string.IsNullOrWhiteSpace(userProfile))
        {
            foreach (var child in CommonRootFolderNames)
            {
                AddRoot(Path.Combine(userProfile, child));
            }
        }

        if (includeDefaultSearchRoots || defaultRootCandidates is not null)
        {
            foreach (var candidate in GetDefaultRootCandidates(defaultRootCandidates))
            {
                AddRoot(candidate);
            }
        }

        return roots;
    }

    private static IEnumerable<string> GetDefaultRootCandidates(IReadOnlyList<string>? defaultRootCandidates)
    {
        if (defaultRootCandidates is not null)
        {
            return defaultRootCandidates;
        }

        var candidates = new List<string>();
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            foreach (var child in CommonRootFolderNames)
            {
                candidates.Add(Path.Combine(root, child));
            }

            if (!string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(root);
            }
        }

        return candidates;
    }

    private static IEnumerable<string> GetChildDirectories(string directory)
    {
        // Streamed (not ToArray()'d) so a caller that breaks early on
        // ShouldStop()/MaxDirectoriesScanned doesn't first pay the cost of
        // enumerating and materializing every entry in a very wide directory.
        IEnumerator<string> enumerator;
        try
        {
            enumerator = Directory.EnumerateDirectories(directory).GetEnumerator();
        }
        catch
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                string current;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    current = enumerator.Current;
                }
                catch
                {
                    yield break;
                }

                yield return current;
            }
        }
    }

    private readonly record struct ScanWorkItem(string Directory, int Depth);

    private static bool IsGitRepository(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git"));

    public static string? TryGetRemoteUrl(string directory) =>
        IsGitRepository(directory) ? TryReadOriginRemoteUrl(directory) : null;

    private static string? TryReadOriginRemoteUrl(string directory)
    {
        var configPath = Path.Combine(directory, ".git", "config");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            string? originUrl = null;
            var inOriginSection = false;

            foreach (var rawLine in File.ReadLines(configPath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('['))
                {
                    inOriginSection = line.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inOriginSection)
                {
                    continue;
                }

                var match = OriginUrlRegex().Match(line);
                if (match.Success)
                {
                    originUrl = NormalizeRemoteUrl(match.Groups[1].Value.Trim());
                    break;
                }
            }

            return originUrl;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeRemoteUrl(string remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return null;
        }

        if (remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return remote.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? remote[..^4]
                : remote;
        }

        var sshMatch = ScpOriginRegex().Match(remote);
        if (!sshMatch.Success)
        {
            return null;
        }

        var host = sshMatch.Groups["host"].Value;
        var path = sshMatch.Groups["path"].Value.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        return $"https://{host}/{path}";
    }

    [GeneratedRegex(@"^url\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginUrlRegex();

    [GeneratedRegex(@"^(?:ssh://)?(?:[^@]+@)(?<host>[^:/]+)[:/](?<path>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ScpOriginRegex();
}
