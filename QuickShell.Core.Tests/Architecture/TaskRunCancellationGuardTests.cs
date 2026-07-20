using System.Linq;

namespace QuickShell.Core.Tests.Architecture;

/// <summary>
/// Fails the build if production code schedules a fire-and-forget <c>Task.Run(...)</c>
/// without passing a cancellation token. Phase 1 replaced every untokened background
/// scheduler (row enrichment, directory-repair probes, suggestion scans, settings
/// refresh, git discovery) with lifetime-aware scheduling; this guard keeps new
/// fire-and-forget sites from reintroducing the gap.
/// </summary>
public sealed class TaskRunCancellationGuardTests
{
    private static readonly string[] ProductionRoots =
    [
        "QuickShell.Core",
        "QuickShell",
        "QuickShell.Run",
        "QuickShell.Suggest",
    ];

    private const string CallPrefix = "Task.Run(";

    [Fact]
    public void Production_code_never_calls_TaskRun_without_a_cancellation_token()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var directory in ProductionRoots.Select(root => Path.Join(repoRoot, root)))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExcludedPath(file))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                var relative = Path.GetRelativePath(repoRoot, file);

                foreach (var args in EnumerateTaskRunArgumentLists(text))
                {
                    if (!ContainsTokenLikeIdentifier(args))
                    {
                        violations.Add($"{relative}: Task.Run(...) call has no CancellationToken argument");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Task.Run(...) calls missing a CancellationToken argument:" + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Finds every <c>Task.Run(</c> call site in <paramref name="text"/> and yields the
    /// full, balanced-paren argument-list text (including any lambda body) for each one.
    /// Lambda bodies routinely contain nested parens/braces, so this scans depth rather
    /// than using a single-line regex.
    /// </summary>
    private static IEnumerable<string> EnumerateTaskRunArgumentLists(string text)
    {
        var searchStart = 0;
        while (true)
        {
            var callStart = text.IndexOf(CallPrefix, searchStart, StringComparison.Ordinal);
            if (callStart < 0)
            {
                yield break;
            }

            var argsStart = callStart + CallPrefix.Length;
            var depth = 1;
            var index = argsStart;
            while (index < text.Length && depth > 0)
            {
                switch (text[index])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        break;
                }

                index++;
            }

            // Unbalanced (shouldn't happen in compiling source) — stop scanning this file.
            if (depth != 0)
            {
                yield break;
            }

            yield return text[argsStart..(index - 1)];
            searchStart = index;
        }
    }

    private static bool ContainsTokenLikeIdentifier(string argumentListText) =>
        argumentListText.Contains("token", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedPath(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".Tests/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "QuickShell.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate QuickShell.sln from test base directory.");
    }
}
