using System.Text.RegularExpressions;

namespace QuickShell.Services;

internal sealed class GitRepoCandidate
{
    public string Directory { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? RemoteUrl { get; init; }
}

internal static partial class GitRepoDiscovery
{
    private const int MaxRepos = 50;
    private const int MaxDirectoriesScanned = 2000;
    private const int MaxDepth = 5;

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

    public static IReadOnlyList<GitRepoCandidate> Discover(IEnumerable<string>? extraRoots = null)
    {
        var roots = BuildSearchRoots(extraRoots);
        var results = new List<GitRepoCandidate>();
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var root in roots)
        {
            if (results.Count >= MaxRepos || scanned >= MaxDirectoriesScanned)
            {
                break;
            }

            ScanDirectory(root, depth: 0, results, seenDirectories, ref scanned);
        }

        return results
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            foreach (var child in new[] { "Projects", "projects", "dev", "Development", "code", "repos", "source", "src", "Documents" })
            {
                AddRoot(Path.Combine(userProfile, child));
            }
        }

        return roots;
    }

    private static void ScanDirectory(
        string directory,
        int depth,
        List<GitRepoCandidate> results,
        HashSet<string> seenDirectories,
        ref int scanned)
    {
        if (results.Count >= MaxRepos || scanned >= MaxDirectoriesScanned || depth > MaxDepth)
        {
            return;
        }

        if (!Directory.Exists(directory))
        {
            return;
        }

        scanned++;

        if (IsGitRepository(directory))
        {
            if (seenDirectories.Add(directory))
            {
                results.Add(new GitRepoCandidate
                {
                    Directory = directory,
                    Name = Path.GetFileName(directory.TrimEnd('\\', '/')),
                    RemoteUrl = TryReadOriginRemoteUrl(directory),
                });
            }

            return;
        }

        IEnumerable<string> childDirectories;
        try
        {
            childDirectories = Directory.EnumerateDirectories(directory);
        }
        catch
        {
            return;
        }

        foreach (var child in childDirectories)
        {
            if (results.Count >= MaxRepos || scanned >= MaxDirectoriesScanned)
            {
                break;
            }

            var name = Path.GetFileName(child);
            if (string.IsNullOrWhiteSpace(name) || SkipDirectoryNames.Contains(name))
            {
                continue;
            }

            ScanDirectory(child, depth + 1, results, seenDirectories, ref scanned);
        }
    }

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
