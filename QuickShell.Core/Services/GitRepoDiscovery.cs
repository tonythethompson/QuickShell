using System.Collections.Concurrent;
using System.Text.RegularExpressions;

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

    internal static Func<IEnumerable<string>>? DefaultRootCandidatesOverride { get; set; }

    internal static bool IncludeDefaultSearchRoots { get; set; } = true;

    public static IReadOnlyList<GitRepoCandidate> Discover(
        IEnumerable<string>? extraRoots = null,
        int maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism)
    {
        var roots = BuildSearchRoots(extraRoots);
        if (roots.Count == 0)
        {
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
            .Select(_ => Task.Run(Worker))
            .ToArray();

        Task.WaitAll(workers);

        return results
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
                signal.Wait();

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
                    Classification = ProjectClassifier.Classify(workItem.Directory),
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
            lock (sync)
            {
                return results.Count >= MaxRepos || scanned >= MaxDirectoriesScanned;
            }
        }
    }

    private static List<string> BuildSearchRoots(IEnumerable<string>? extraRoots)
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

        if (IncludeDefaultSearchRoots && !string.IsNullOrWhiteSpace(userProfile))
        {
            foreach (var child in CommonRootFolderNames)
            {
                AddRoot(Path.Combine(userProfile, child));
            }
        }

        if (IncludeDefaultSearchRoots || DefaultRootCandidatesOverride is not null)
        {
            foreach (var candidate in GetDefaultRootCandidates())
            {
                AddRoot(candidate);
            }
        }

        return roots;
    }

    private static IEnumerable<string> GetDefaultRootCandidates()
    {
        if (DefaultRootCandidatesOverride is { } overrideCandidates)
        {
            return overrideCandidates();
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
