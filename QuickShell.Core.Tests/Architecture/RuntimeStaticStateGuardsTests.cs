using System.Linq;
using System.Text.RegularExpressions;

namespace QuickShell.Core.Tests.Architecture;

/// <summary>
/// Fails the build if production code reintroduces process-wide mutable service seams
/// that break parallel tests and multi-host DI.
/// </summary>
public sealed class RuntimeStaticStateGuardsTests
{
    private static readonly string[] ProductionRoots =
    [
        "QuickShell.Core",
        "QuickShell",
        "QuickShell.Run",
        "QuickShell.Suggest",
    ];

    private static readonly string[] BannedSubstrings =
    [
        "StartProcessOverride",
        "ProjectAnalysisAccessor",
        "QuickShellServices.Current",
        "WorkspaceHealthCheck.Default",
        "GitRunOverride",
        "GitStatusOverride",
        "ExecutableExistsOverride",
        "PortInUseOverride",
        "ProcessNamesOverride",
        "WslDistroNamesOverride",
        "TryLaunchOverride",
        "WorkspaceGitLaunchGate.ResetForTests",
    ];

    // Heuristic: constructor parameters typed as IServiceProvider in host/core service code.
    private static readonly Regex ServiceProviderCtorParam = new(
        @"\b(public|internal|private)\s+[\w.<>,\s]+\s*\([^)]*\bIServiceProvider\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Production_code_has_no_banned_runtime_static_service_seams()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var directory in ProductionRoots.Select(root => Path.Combine(repoRoot, root)))
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

                foreach (var banned in BannedSubstrings)
                {
                    if (text.Contains(banned, StringComparison.Ordinal))
                    {
                        violations.Add($"{relative}: contains '{banned}'");
                    }
                }

                if (ServiceProviderCtorParam.IsMatch(text)
                    && (relative.Contains($"{Path.DirectorySeparatorChar}Pages{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        || relative.Contains($"{Path.DirectorySeparatorChar}Commands{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        || relative.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
                {
                    violations.Add($"{relative}: constructor appears to take IServiceProvider");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Banned runtime static service seams found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

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
