using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Detectors;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

[Collection(GitRepoIndexIsolation.Name)]
public sealed class GitRepoDiscoveryTests : IDisposable
{
    private readonly string _root;

    public GitRepoDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-git-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        GitRepoDiscovery.IncludeDefaultSearchRoots = false;
        GitRepoDiscovery.DefaultRootCandidatesOverride = () => [];
    }

    [Fact]
    public void Discover_FindsGitRepositoriesUnderProvidedRoot()
    {
        var repoPath = Path.Combine(_root, "sample-repo");
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));

        var discovered = GitRepoDiscovery.Discover([_root]);

        Assert.Contains(discovered, candidate =>
            string.Equals(candidate.Directory, repoPath, StringComparison.OrdinalIgnoreCase)
            && candidate.Name == "sample-repo");
    }

    [Fact]
    public void Discover_ReadsHttpsOriginRemote()
    {
        var repoPath = Path.Combine(_root, "with-remote");
        var gitPath = Path.Combine(repoPath, ".git");
        Directory.CreateDirectory(gitPath);
        File.WriteAllText(
            Path.Combine(gitPath, "config"),
            """
            [remote "origin"]
                url = https://github.com/example/sample.git
            """);

        var discovered = GitRepoDiscovery.Discover([_root]).Single();

        Assert.Equal("https://github.com/example/sample", discovered.RemoteUrl);
    }

    [Fact]
    public void Discover_SkipsNestedSearchInsideGitRepository()
    {
        var repoPath = Path.Combine(_root, "outer");
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));
        Directory.CreateDirectory(Path.Combine(repoPath, "nested", ".git"));

        var discovered = GitRepoDiscovery.Discover([_root]);

        Assert.Single(discovered);
        Assert.Equal("outer", discovered[0].Name);
    }

    [Fact]
    public void Discover_WithBoundedParallelism_FindsRepositoriesAcrossSiblingBranches()
    {
        var frontendRepo = Path.Combine(_root, "apps", "frontend");
        var backendRepo = Path.Combine(_root, "services", "backend");
        Directory.CreateDirectory(Path.Combine(frontendRepo, ".git"));
        Directory.CreateDirectory(Path.Combine(backendRepo, ".git"));

        var discovered = GitRepoDiscovery.Discover([_root], maxDegreeOfParallelism: 2);

        Assert.Contains(discovered, candidate =>
            string.Equals(candidate.Directory, frontendRepo, StringComparison.OrdinalIgnoreCase)
            && candidate.Name == "frontend");
        Assert.Contains(discovered, candidate =>
            string.Equals(candidate.Directory, backendRepo, StringComparison.OrdinalIgnoreCase)
            && candidate.Name == "backend");
    }

    [Fact]
    public void Discover_WithBoundedParallelism_KeepsStableNameOrdering()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zeta", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha", ".git"));

        var discovered = GitRepoDiscovery.Discover([_root], maxDegreeOfParallelism: 4);

        Assert.Collection(
            discovered,
            candidate => Assert.Equal("alpha", candidate.Name),
            candidate => Assert.Equal("zeta", candidate.Name));
    }

    [Fact]
    public void Discover_UsesDefaultRootCandidatesWhenNoExplicitRootProvided()
    {
        var repoPath = Path.Combine(_root, "default-root-repo");
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));
        GitRepoDiscovery.DefaultRootCandidatesOverride = () => [_root];

        var discovered = GitRepoDiscovery.Discover();

        Assert.Contains(discovered, candidate =>
            string.Equals(candidate.Directory, repoPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discover_MergesExtraRootsWithDefaultLocations()
    {
        var extraRoot = Path.Combine(_root, "shortcuts");
        var defaultRoot = Path.Combine(_root, "defaults");
        Directory.CreateDirectory(Path.Combine(extraRoot, "from-shortcut", ".git"));
        Directory.CreateDirectory(Path.Combine(defaultRoot, "from-default", ".git"));
        GitRepoDiscovery.DefaultRootCandidatesOverride = () => [defaultRoot];

        var discovered = GitRepoDiscovery.Discover([extraRoot]);

        Assert.Contains(discovered, candidate => candidate.Name == "from-shortcut");
        Assert.Contains(discovered, candidate => candidate.Name == "from-default");
    }

    [Fact]
    public void Discover_FindsExplicitWorkspaceRootWhenSiblingRootExhaustsBudget()
    {
        var siblingRoot = Path.Combine(_root, "wide-parent");
        var workspaceRoot = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(siblingRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));

        for (var i = 0; i < 2100; i++)
        {
            Directory.CreateDirectory(Path.Combine(siblingRoot, $"child-{i:D4}"));
        }

        var discovered = GitRepoDiscovery.Discover([siblingRoot, workspaceRoot]);

        Assert.Contains(discovered, candidate =>
            string.Equals(candidate.Directory, workspaceRoot, StringComparison.OrdinalIgnoreCase)
            && candidate.Name == "workspace");
    }

    public void Dispose()
    {
        GitRepoDiscovery.DefaultRootCandidatesOverride = null;
        GitRepoDiscovery.IncludeDefaultSearchRoots = true;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup for temp test data.
        }
    }
}

public sealed class ShortcutRecentsTests
{
    [Fact]
    public void GetRecentWorkspaces_OrdersByLastUsedAndSkipsPinned()
    {
        var shortcuts = new List<TerminalShortcut>
        {
            new()
            {
                Id = "1",
                Name = "Old",
            },
            new()
            {
                Id = "2",
                Name = "Recent",
                LastUsedUtc = DateTime.UtcNow.AddHours(-1),
            },
            new()
            {
                Id = "3",
                Name = "Pinned recent",
                IsPinned = true,
                LastUsedUtc = DateTime.UtcNow,
            },
            new()
            {
                Id = "4",
                Name = "Never used",
            },
        };

        var recents = ShortcutRecents.GetRecentWorkspaces(shortcuts);

        Assert.Single(recents);
        Assert.Equal("Recent", recents[0].Name);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 8)]
    [InlineData(8, 8)]
    [InlineData(100, 8)]
    [InlineData(150, 8)]
    public void NormalizeCount_EnablesFixedCapOrDisables(int input, int expected) =>
        Assert.Equal(expected, QuickShellRecentSettings.NormalizeCount(input));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(12, 8)]
    public void ClampDisplayCount_LimitsToEnabledCap(int input, int expected) =>
        Assert.Equal(expected, QuickShellRecentSettings.ClampDisplayCount(input));

    [Fact]
    public void TryParseCount_PrefersInvariantCultureDigits()
    {
        Assert.True(QuickShellRecentSettings.TryParseCount("12", out var parsed));
        Assert.Equal(8, parsed);
        Assert.Equal("8", QuickShellRecentSettings.FormatCount(12));
    }

    [Fact]
    public void GetRecentWorkspaces_RespectsMaxCount()
    {
        var shortcuts = Enumerable.Range(1, 12)
            .Select(index => new TerminalShortcut
            {
                Id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = $"Workspace {index}",
                LastUsedUtc = DateTime.UtcNow.AddMinutes(-index),
            })
            .ToList();

        Assert.Equal(3, ShortcutRecents.GetRecentWorkspaces(shortcuts, maxCount: 3).Count);
        Assert.Empty(ShortcutRecents.GetRecentWorkspaces(shortcuts, maxCount: 0));
    }
}

public sealed class WorkspaceLinkValidationTests
{
    [Fact]
    public void TryValidateOptionalLinkUrl_AcceptsHttpAndHttps()
    {
        Assert.True(ShortcutValidation.TryValidateOptionalLinkUrl("http://localhost:3000", out _, out var normalized));
        Assert.Equal("http://localhost:3000/", normalized);

        Assert.True(ShortcutValidation.TryValidateOptionalLinkUrl("https://github.com/example/repo", out _, out normalized));
        Assert.Equal("https://github.com/example/repo", normalized);
    }

    [Fact]
    public void TryValidateOptionalLinkUrl_RejectsNonHttpSchemes()
    {
        Assert.False(ShortcutValidation.TryValidateOptionalLinkUrl("file:///C:/temp", out var error, out _));
        Assert.Contains("http", error, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DevServerUrlDetectionTests : IDisposable
{
    private readonly string _root;

    public DevServerUrlDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-dev-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryDetectDevServerUrl_ReadsExplicitPortFromDevScript()
    {
        WritePackageJson("""
        {
          "scripts": {
            "dev": "vite --port 4321"
          }
        }
        """);

        var url = DevServerUrlDetection.TryDetectDevServerUrl(_root);

        Assert.Equal("http://localhost:4321", url);
    }

    [Fact]
    public void TryDetectDevServerUrl_UsesViteDefaultWhenNoPortSpecified()
    {
        WritePackageJson("""
        {
          "devDependencies": {
            "vite": "^6.0.0"
          },
          "scripts": {
            "dev": "vite"
          }
        }
        """);

        var url = DevServerUrlDetection.TryDetectDevServerUrl(_root);

        Assert.Equal("http://localhost:5173", url);
    }

    [Fact]
    public void TryDetectDevServerUrl_ReadsUtf16BomPackageJson()
    {
        var json = """
        {
          "scripts": {
            "dev": "vite --port 4321"
          }
        }
        """;
        var path = Path.Combine(_root, "package.json");
        using (var stream = File.Create(path))
        {
            var preamble = new byte[] { 0xFF, 0xFE };
            stream.Write(preamble);
            var content = System.Text.Encoding.Unicode.GetBytes(json);
            stream.Write(content);
        }

        Assert.Equal("http://localhost:4321", DevServerUrlDetection.TryDetectDevServerUrl(_root));
    }

    [Fact]
    public void TryDetectDevServerUrl_ReturnsNullWhenNoPackageJson()
    {
        Assert.Null(DevServerUrlDetection.TryDetectDevServerUrl(_root));
    }

    [Fact]
    public void TryDetectDevLaunchCommand_UsesNpmByDefault()
    {
        WritePackageJson("""
        {
          "scripts": {
            "dev": "vite"
          }
        }
        """);

        Assert.Equal("npm run dev", DevServerUrlDetection.TryDetectDevLaunchCommand(_root));
    }

    [Fact]
    public void TryDetectDevLaunchCommand_UsesPnpmWhenLockfilePresent()
    {
        WritePackageJson("""
        {
          "scripts": {
            "dev": "vite"
          }
        }
        """);
        File.WriteAllText(Path.Combine(_root, "pnpm-lock.yaml"), string.Empty);

        Assert.Equal("pnpm dev", DevServerUrlDetection.TryDetectDevLaunchCommand(_root));
    }

    [Fact]
    public void FormatPackageScriptCommand_UsesYarnWhenYarnLockExists()
    {
        File.WriteAllText(Path.Combine(_root, "yarn.lock"), string.Empty);

        Assert.Equal("yarn dev", DevServerUrlDetection.FormatPackageScriptCommand(_root, "dev"));
    }

    [Fact]
    public void TryDetectDevLaunchCommand_FallsBackToStartScript()
    {
        WritePackageJson("""
        {
          "scripts": {
            "start": "react-scripts start"
          },
          "dependencies": {
            "react-scripts": "5.0.1"
          }
        }
        """);

        Assert.Equal("npm start", DevServerUrlDetection.TryDetectDevLaunchCommand(_root));
        Assert.Equal("http://localhost:3000", DevServerUrlDetection.TryDetectDevServerUrl(_root));
    }

    [Fact]
    public void TryDetectDevLaunchCommand_ReturnsNullWhenNoScripts()
    {
        WritePackageJson("""
        {
          "scripts": {}
        }
        """);

        Assert.Null(DevServerUrlDetection.TryDetectDevLaunchCommand(_root));
    }

    [Fact]
    public void ApplyDirectoryHints_SyncsDetectedDevCommandToLaunchEntry()
    {
        WritePackageJson("""
        {
          "scripts": {
            "dev": "vite"
          }
        }
        """);

        var seed = WorkspaceSeedFactory.ApplyDirectoryHints(new TerminalShortcut
        {
            Name = "sample",
            Directory = _root,
            Launches = [],
        });

        Assert.Equal("npm run dev", seed.Command);
        Assert.Single(seed.Launches);
        Assert.Equal("npm run dev", seed.Launches[0].Command);
        Assert.Equal(TaskTypeCatalog.Frontend, seed.Launches[0].TaskType);
    }

    [Fact]
    public void ApplyDirectoryHints_DoesNotOverwriteExplicitlySetTaskType()
    {
        WritePackageJson("""
        {
          "scripts": {
            "dev": "vite"
          }
        }
        """);

        var seed = WorkspaceSeedFactory.ApplyDirectoryHints(new TerminalShortcut
        {
            Name = "sample",
            Directory = _root,
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Main",
                    IsEnabled = true,
                    Order = 0,
                    TaskType = TaskTypeCatalog.Database,
                },
            ],
        });

        Assert.Single(seed.Launches);
        Assert.Equal(TaskTypeCatalog.Database, seed.Launches[0].TaskType);
    }

    private void WritePackageJson(string contents) =>
        File.WriteAllText(Path.Combine(_root, "package.json"), contents);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitRepoIndexIsolation
{
    public const string Name = "GitRepoIndex";
}

[Collection(GitRepoIndexIsolation.Name)]
public sealed class GitRepoIndexTests : IDisposable
{
    private readonly string _root;

    public GitRepoIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-git-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        GitRepoIndex.Invalidate();
    }

    [Fact]
    public void Search_FiltersByNameAndSkipsSavedDirectories()
    {
        var repoPath = Path.Combine(_root, "alpha-app");
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));

        GitRepoIndex.Invalidate();
        var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { repoPath };

        _ = GitRepoIndex.Search("alpha", [_root], saved);
        GitRepoIndex.WaitForPopulationForTests(_root, TimeSpan.FromSeconds(10));

        var matches = GitRepoIndex.Search("alpha", [_root], saved);

        Assert.Empty(matches);

        matches = GitRepoIndex.Search("alpha", [_root], savedDirectories: null);

        Assert.Single(matches);
        Assert.Equal("alpha-app", matches[0].Name);
    }

    [Fact]
    public void GetAll_RefreshesWhenExtraRootsChange()
    {
        var firstRoot = Path.Combine(_root, "first");
        var secondRoot = Path.Combine(_root, "second");
        var firstRepo = Path.Combine(firstRoot, "alpha-app");
        var secondRepo = Path.Combine(secondRoot, "beta-app");
        Directory.CreateDirectory(Path.Combine(firstRepo, ".git"));
        Directory.CreateDirectory(Path.Combine(secondRepo, ".git"));

        _ = GitRepoIndex.GetAll([firstRoot]);
        GitRepoIndex.WaitForPopulationForTests(BuildRootKeyForTest(firstRoot), TimeSpan.FromSeconds(10));
        var first = GitRepoIndex.GetAll([firstRoot]);

        _ = GitRepoIndex.GetAll([secondRoot]);
        GitRepoIndex.WaitForPopulationForTests(BuildRootKeyForTest(secondRoot), TimeSpan.FromSeconds(10));
        var second = GitRepoIndex.GetAll([secondRoot]);

        Assert.Contains(first, candidate => candidate.Name == "alpha-app");
        Assert.DoesNotContain(second, candidate => candidate.Name == "alpha-app");
        Assert.Contains(second, candidate => candidate.Name == "beta-app");
    }

    public void Dispose()
    {
        GitRepoIndex.Invalidate();
        GitRepoDiscovery.DefaultRootCandidatesOverride = null;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static string BuildRootKeyForTest(string root) => root;
}

public sealed class GitRepoSearchRootsTests
{
    [Fact]
    public void FromShortcuts_IncludesWorkspaceDirectoryAndParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "quickshell-roots-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "app");

        var roots = GitRepoSearchRoots.FromShortcuts(
        [
            new TerminalShortcut
            {
                Name = "App",
                Directory = workspace,
            },
        ]).ToArray();

        Assert.Contains(workspace, roots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(parent, roots, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromShortcuts_ExcludesDriveRootParent()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())
            ?? throw new InvalidOperationException("Could not resolve temp drive root.");
        var workspace = Path.Combine(driveRoot, "quickshell-drive-root-" + Guid.NewGuid().ToString("N"));

        var roots = GitRepoSearchRoots.FromShortcuts(
        [
            new TerminalShortcut
            {
                Name = "Drive workspace",
                Directory = workspace,
            },
        ]).ToArray();

        Assert.Contains(workspace, roots, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(driveRoot, roots, StringComparer.OrdinalIgnoreCase);
    }
}

[Collection(ProjectAnalysisStaticStateIsolation.Name)]
public sealed class CompanionAppTests : IDisposable
{
    private readonly string _root;
    private readonly CompanionAppDetector _companionAppDetector = new();

    public CompanionAppTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-companion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CompanionAppCatalog.TryResolveExecutableOverride = null;
        CompanionAppPreference.ReadLastUsedOverride = null;
        CompanionAppPreference.WriteLastUsedOverride = null;
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersVsCodeWhenDotVscodeExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
        CompanionAppPreference.ReadLastUsedOverride = () => null;

        try
        {
            var suggestion = _companionAppDetector.TrySuggest(_root);

            if (CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCode) is not null)
            {
                Assert.NotNull(suggestion);
                Assert.Equal(CompanionAppCatalog.PresetVsCode, suggestion!.PresetId);
                Assert.Equal(".", suggestion.Arguments);
                Assert.True(suggestion.EnableOnLaunch);
                return;
            }

            if (CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCodeInsiders) is not null)
            {
                Assert.NotNull(suggestion);
                Assert.Equal(CompanionAppCatalog.PresetVsCodeInsiders, suggestion!.PresetId);
                return;
            }

            Assert.Null(suggestion);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersTraeWhenTraeMarkerExists()
    {
        Directory.CreateDirectory(Path.Join(_root, ".trae"));
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            string.Equals(preset, CompanionAppCatalog.PresetTrae, StringComparison.OrdinalIgnoreCase)
                ? @"C:\fake\Trae.exe"
                : null;

        var suggestion = new QuickShell.Classification.Detectors.CompanionAppDetector()
            .TrySuggest(_root);

        Assert.NotNull(suggestion);
        Assert.Equal(CompanionAppCatalog.PresetTrae, suggestion!.PresetId);
        Assert.Equal(".", suggestion.Arguments);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersCursorOverTrae()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".cursor"));
        Directory.CreateDirectory(Path.Join(_root, ".trae"));
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            preset is CompanionAppCatalog.PresetCursor or CompanionAppCatalog.PresetTrae
                ? $@"C:\fake\{preset}.exe"
                : null;

        var suggestion = new QuickShell.Classification.Detectors.CompanionAppDetector()
            .TrySuggest(_root);

        Assert.NotNull(suggestion);
        Assert.Equal(CompanionAppCatalog.PresetCursor, suggestion!.PresetId);
    }

    [Fact]
    public void TrySuggestFromDirectory_FallsThroughWhenHigherPriorityCompanionMissing()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".cursor"));
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
        CompanionAppPreference.ReadLastUsedOverride = () => null;

        try
        {
            var cursorInstalled = CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetCursor) is not null;
            var vsCodeInstalled = CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCode) is not null;
            var insidersInstalled = CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCodeInsiders) is not null;
            var suggestion = _companionAppDetector.TrySuggest(_root);

            if (!cursorInstalled && vsCodeInstalled)
            {
                Assert.NotNull(suggestion);
                Assert.Equal(CompanionAppCatalog.PresetVsCode, suggestion!.PresetId);
                return;
            }

            if (!cursorInstalled && !vsCodeInstalled && insidersInstalled)
            {
                Assert.NotNull(suggestion);
                Assert.Equal(CompanionAppCatalog.PresetVsCodeInsiders, suggestion!.PresetId);
                return;
            }

            if (cursorInstalled)
            {
                Assert.NotNull(suggestion);
                Assert.Equal(CompanionAppCatalog.PresetCursor, suggestion!.PresetId);
                return;
            }

            if (!vsCodeInstalled && !insidersInstalled)
            {
                Assert.Null(suggestion);
            }
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
        }
    }

    [Fact]
    public void ExpandArguments_ReplacesFolderTokenAndDot()
    {
        var directory = @"C:\Projects\sample app";

        Assert.Equal("\"C:\\Projects\\sample app\"", CompanionAppLauncher.ExpandArguments(".", directory));
        Assert.Equal(
            "\"C:\\Projects\\sample app\" --new-window",
            CompanionAppLauncher.ExpandArguments("{folder} --new-window", directory));
        Assert.Equal("C:\\Projects\\sample", CompanionAppLauncher.ExpandArguments(".", @"C:\Projects\sample"));
    }

    [Fact]
    public void ExpandArguments_ReplacesSolutionTokenWithSlnOrFolder()
    {
        var directory = Path.Combine(_root, "sample app");
        Directory.CreateDirectory(directory);
        var solutionPath = Path.Combine(directory, "App.sln");
        File.WriteAllText(solutionPath, string.Empty);

        Assert.Equal($"\"{solutionPath}\"", CompanionAppLauncher.ExpandArguments("{solution}", directory));
        Assert.Equal(_root, CompanionAppLauncher.ExpandArguments("{solution}", _root));
    }

    [Fact]
    public void BuildFormChoicesJson_AlwaysIncludesExplorerOnWindows()
    {
        using var document = JsonDocument.Parse(CompanionAppCatalog.BuildFormChoicesJson());
        var values = document.RootElement
            .EnumerateArray()
            .Select(choice => choice.GetProperty("value").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(CompanionAppCatalog.PresetExplorer, values);
    }

    [Fact]
    public void BuildFormChoicesJson_OnlyIncludesInstalledPresets()
    {
        using var document = JsonDocument.Parse(CompanionAppCatalog.BuildFormChoicesJson());
        var choices = document.RootElement.EnumerateArray().ToList();
        var values = choices
            .Select(choice => choice.GetProperty("value").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var titlesByValue = choices.ToDictionary(
            choice => choice.GetProperty("value").GetString()!,
            choice => choice.GetProperty("title").GetString(),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains(CompanionAppCatalog.PresetNone, values);
        Assert.Contains(CompanionAppCatalog.PresetCustom, values);
        Assert.Equal(CompanionAppCatalog.FormChoiceTitleNone, titlesByValue[CompanionAppCatalog.PresetNone]);
        Assert.Equal(CompanionAppCatalog.FormChoiceTitleCustom, titlesByValue[CompanionAppCatalog.PresetCustom]);

        if (CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetVsCode))
        {
            Assert.Contains(CompanionAppCatalog.PresetVsCode, values);
        }
        else
        {
            Assert.DoesNotContain(CompanionAppCatalog.PresetVsCode, values);
        }
    }

    [Fact]
    public void ToFormPresetValue_PreservesCustomPresetWithoutPath()
    {
        Assert.Equal(
            CompanionAppCatalog.PresetCustom,
            CompanionAppCatalog.ToFormPresetValue(CompanionAppCatalog.PresetCustom, executablePath: null));
    }

    [Fact]
    public void ToFormPresetValue_KeepsCustomWhenStoredAsCustom()
    {
        Assert.Equal(
            CompanionAppCatalog.PresetCustom,
            CompanionAppCatalog.ToFormPresetValue(
                CompanionAppCatalog.PresetCustom,
                @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe"));
    }

    [Fact]
    public void ResolvePresetAfterBrowse_MatchesCatalogPresetOrFallsBackToCustom()
    {
        Assert.Equal(
            CompanionAppCatalog.PresetVsCode,
            CompanionAppCatalog.ResolvePresetAfterBrowse(
                @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe"));

        Assert.Equal(
            CompanionAppCatalog.PresetCustom,
            CompanionAppCatalog.ResolvePresetAfterBrowse(@"C:\Tools\MyCustomApp.exe"));
    }

    [Fact]
    public void ShouldShowExecutablePath_WhenPathIsSet()
    {
        Assert.True(CompanionAppCatalog.ShouldShowExecutablePath(@"C:\Apps\Code.exe"));
        Assert.False(CompanionAppCatalog.ShouldShowExecutablePath(null));
        Assert.False(CompanionAppCatalog.ShouldShowExecutablePath(""));
    }

    [Fact]
    public void TryValidateFormSelection_RequiresBrowseWhenCustomWithoutPath()
    {
        Assert.False(CompanionAppCatalog.TryValidateFormSelection(
            CompanionAppCatalog.PresetCustom,
            null,
            out var error));
        Assert.Equal(CompanionAppCatalog.BrowseRequiredMessage, error);
        Assert.True(CompanionAppCatalog.TryValidateFormSelection(
            CompanionAppCatalog.PresetCustom,
            @"C:\Apps\Code.exe",
            out _));
        Assert.True(CompanionAppCatalog.TryValidateFormSelection(
            CompanionAppCatalog.PresetNone,
            null,
            out _));
    }

    [Fact]
    public void NormalizePresetForForm_FallsBackWhenCatalogPresetMissing()
    {
        if (CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetVsCode))
        {
            Assert.Equal(
                CompanionAppCatalog.PresetVsCode,
                CompanionAppCatalog.NormalizePresetForForm(
                    CompanionAppCatalog.PresetVsCode,
                    @"C:\Apps\Code.exe"));
            return;
        }

        Assert.Equal(
            CompanionAppCatalog.PresetCustom,
            CompanionAppCatalog.NormalizePresetForForm(
                CompanionAppCatalog.PresetVsCode,
                @"C:\Apps\Code.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetNone,
            CompanionAppCatalog.NormalizePresetForForm(
                CompanionAppCatalog.PresetVsCode,
                executablePath: null));
    }

    [Fact]
    public void GetContextMenuIcon_UsesOpenWithForAllApps()
    {
        Assert.Equal(
            ShortcutGlyphs.OpenCompanionApp,
            CompanionAppCatalog.GetContextMenuIcon(
                @"C:\Users\me\AppData\Local\Programs\cursor\Cursor.exe"));
        Assert.Equal(
            ShortcutGlyphs.OpenCompanionApp,
            CompanionAppCatalog.GetContextMenuIcon(@"C:\Tools\MyCustomApp.exe"));
        Assert.Equal("\uE7AC", ShortcutGlyphs.OpenCompanionApp);
    }

    [Fact]
    public void TryValidateCompanionApp_RequiresPathWhenLaunchEnabled()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Sample",
            Directory = _root,
            OpenCompanionAppOnLaunch = true,
        };

        Assert.False(ShortcutValidation.TryValidateCompanionApp(shortcut, out var error));
        Assert.Contains("required", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferPresetFromPath_RecognizesKnownEditors()
    {
        Assert.Equal(
            CompanionAppCatalog.PresetVsCode,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\Microsoft VS Code\Code.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetVsCodeInsiders,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\Microsoft VS Code Insiders\Code - Insiders.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetFork,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\Fork\Fork.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetGitKraken,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Users\demo\AppData\Local\gitkraken\gitkraken.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetSourcetree,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Users\demo\AppData\Local\SourceTree\SourceTree.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetRider,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\JetBrains\Rider\bin\rider64.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetIntelliJIdea,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\JetBrains\IntelliJ IDEA\bin\idea64.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetWebStorm,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\JetBrains\WebStorm\bin\webstorm64.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetPyCharm,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\JetBrains\PyCharm\bin\pycharm64.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetAndroidStudio,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Program Files\Android\Android Studio\bin\studio64.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetDevin,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Users\demo\AppData\Local\Programs\Windsurf\Windsurf.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetKiro,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Users\demo\AppData\Local\Programs\Kiro\Kiro.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetZed,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\Zed\zed.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetNotepadPlusPlus,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Program Files\Notepad++\notepad++.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetVs2022,
            CompanionAppCatalog.InferPresetFromPath(
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetCustom,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\MyEditor.exe"));
    }

    [Fact]
    public void GetDefaultArguments_NotepadPlusPlus_UsesFolderToken()
    {
        Assert.Equal("{folder}", CompanionAppCatalog.GetDefaultArguments(CompanionAppCatalog.PresetNotepadPlusPlus));
    }

    [Fact]
    public void PreferLastUsed_MovesMatchingPresetToFront()
    {
        CompanionAppPreference.ReadLastUsedOverride = () => CompanionAppCatalog.PresetWebStorm;
        try
        {
            var ordered = CompanionAppPreference.PreferLastUsed(
            [
                CompanionAppCatalog.PresetPyCharm,
                CompanionAppCatalog.PresetWebStorm,
                CompanionAppCatalog.PresetIntelliJIdea,
            ]);

            Assert.Equal(CompanionAppCatalog.PresetWebStorm, ordered[0]);
            Assert.Equal(CompanionAppCatalog.PresetPyCharm, ordered[1]);
            Assert.Equal(CompanionAppCatalog.PresetIntelliJIdea, ordered[2]);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersWebStormForPackageJsonIdeaProjectsWhenInstalled()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "package.json"), "{}");
        CompanionAppPreference.ReadLastUsedOverride = () => null;
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            preset is CompanionAppCatalog.PresetWebStorm or CompanionAppCatalog.PresetIntelliJIdea
                ? $@"C:\fake\{preset}.exe"
                : null;

        try
        {
            var suggestion = _companionAppDetector.TrySuggest(_root);
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetWebStorm, suggestion!.PresetId);
            Assert.Equal("{folder}", suggestion.Arguments);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
            CompanionAppCatalog.TryResolveExecutableOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_FallsBackToIdeaWhenWebStormMissing()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "package.json"), "{}");
        CompanionAppPreference.ReadLastUsedOverride = () => null;
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            string.Equals(preset, CompanionAppCatalog.PresetIntelliJIdea, StringComparison.OrdinalIgnoreCase)
                ? @"C:\fake\idea64.exe"
                : null;

        try
        {
            var suggestion = _companionAppDetector.TrySuggest(_root);
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetIntelliJIdea, suggestion!.PresetId);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
            CompanionAppCatalog.TryResolveExecutableOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersAndroidStudioForGradleIdeaProjects()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "build.gradle"), string.Empty);
        CompanionAppPreference.ReadLastUsedOverride = () => null;
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            preset is CompanionAppCatalog.PresetAndroidStudio or CompanionAppCatalog.PresetIntelliJIdea
                ? $@"C:\fake\{preset}.exe"
                : null;

        try
        {
            var suggestion = _companionAppDetector.TrySuggest(_root);
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetAndroidStudio, suggestion!.PresetId);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
            CompanionAppCatalog.TryResolveExecutableOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersPyCharmForPyprojectIdeaProjects()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "pyproject.toml"), "[project]\nname = \"demo\"\n");
        CompanionAppPreference.ReadLastUsedOverride = () => null;
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            preset is CompanionAppCatalog.PresetPyCharm or CompanionAppCatalog.PresetIntelliJIdea
                ? $@"C:\fake\{preset}.exe"
                : null;

        try
        {
            var suggestion = _companionAppDetector.TrySuggest(_root);
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetPyCharm, suggestion!.PresetId);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
            CompanionAppCatalog.TryResolveExecutableOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersLastUsedAmongJetBrainsCandidates()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "pyproject.toml"), "[project]\nname = \"demo\"\n");
        CompanionAppPreference.ReadLastUsedOverride = () => CompanionAppCatalog.PresetIntelliJIdea;
        CompanionAppCatalog.TryResolveExecutableOverride = preset =>
            preset is CompanionAppCatalog.PresetWebStorm
                or CompanionAppCatalog.PresetPyCharm
                or CompanionAppCatalog.PresetIntelliJIdea
                ? $@"C:\fake\{preset}.exe"
                : null;

        try
        {
            var suggestion = _companionAppDetector.TrySuggest(_root);
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetIntelliJIdea, suggestion!.PresetId);
        }
        finally
        {
            CompanionAppPreference.ReadLastUsedOverride = null;
            CompanionAppCatalog.TryResolveExecutableOverride = null;
        }
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersObsidianWhenVaultMarkerExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".obsidian"));

        var suggestion = _companionAppDetector.TrySuggest(_root);

        if (CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetObsidian) is null)
        {
            Assert.Null(suggestion);
            return;
        }

        Assert.Equal(CompanionAppCatalog.PresetObsidian, suggestion!.PresetId);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersGitClientWhenRepositoryExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));

        var suggestion = _companionAppDetector.TrySuggest(_root);
        if (suggestion is null)
        {
            Assert.False(CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetFork)
                || CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetGitKraken)
                || CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetSourcetree)
                || CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetGitHubDesktop));
            return;
        }

        Assert.True(
            suggestion.PresetId is CompanionAppCatalog.PresetFork
                or CompanionAppCatalog.PresetGitKraken
                or CompanionAppCatalog.PresetSourcetree
                or CompanionAppCatalog.PresetGitHubDesktop);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersVisualStudioWhenSolutionExists()
    {
        File.WriteAllText(Path.Combine(_root, "App.sln"), string.Empty);

        var suggestion = _companionAppDetector.TrySuggest(_root);
        if (suggestion is null)
        {
            Assert.False(CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetVs2022)
                && CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetVs2026));
            return;
        }

        Assert.True(
            suggestion.PresetId is CompanionAppCatalog.PresetVs2022 or CompanionAppCatalog.PresetVs2026);
        Assert.Equal("{solution}", suggestion.Arguments);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersRiderForDotNetIdeaProjects()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project />");

        var suggestion = _companionAppDetector.TrySuggest(_root);
        if (suggestion is null)
        {
            Assert.False(CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetRider));
            return;
        }

        Assert.Equal(CompanionAppCatalog.PresetRider, suggestion.PresetId);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersIntelliJForIdeaProjectsWithoutDotNet()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".idea"));
        File.WriteAllText(Path.Combine(_root, "pom.xml"), "<project />");

        var suggestion = _companionAppDetector.TrySuggest(_root);
        if (suggestion is null)
        {
            Assert.False(CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetIntelliJIdea));
            return;
        }

        Assert.Equal(CompanionAppCatalog.PresetIntelliJIdea, suggestion.PresetId);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersZedWhenZedMarkerExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".zed"));

        var suggestion = _companionAppDetector.TrySuggest(_root);
        if (suggestion is null)
        {
            Assert.False(CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetZed));
            return;
        }

        Assert.Equal(CompanionAppCatalog.PresetZed, suggestion.PresetId);
    }

    [Fact]
    public void CreateStateFromPreset_Explorer_UsesFolderArgument()
    {
        if (!CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetExplorer))
        {
            return;
        }

        var state = CompanionAppCatalog.CreateStateFromPreset(CompanionAppCatalog.PresetExplorer);

        Assert.True(state.LaunchOnWorkspaceOpen);
        Assert.Equal("{folder}", state.Arguments);
    }

    [Fact]
    public void ReconcileStoredShortcut_WhenLaunchDisabled_PreservesCompanionPath()
    {
        var state = CompanionAppCatalog.ReconcileStoredShortcut(
            openOnLaunch: false,
            @"C:\Apps\MyEditor.exe",
            ".");

        Assert.Equal(CompanionAppCatalog.PresetCustom, state.Preset);
        Assert.False(state.LaunchOnWorkspaceOpen);
        Assert.Equal(@"C:\Apps\MyEditor.exe", state.Path);
        Assert.Equal(".", state.Arguments);
    }

    [Fact]
    public void ReconcileForSave_PreservesCustomPathWhenLaunchDisabled()
    {
        var state = CompanionAppCatalog.ReconcileForSave(
            CompanionAppCatalog.PresetCustom,
            @"C:\Missing\MyEditor.exe",
            ".",
            openOnLaunch: false);

        Assert.Equal(CompanionAppCatalog.PresetCustom, state.Preset);
        Assert.False(state.LaunchOnWorkspaceOpen);
        Assert.Equal(@"C:\Missing\MyEditor.exe", state.Path);
    }

    [Fact]
    public void ReconcileForForm_StaleCustomPath_DisablesLaunch()
    {
        var state = CompanionAppCatalog.ReconcileForForm(
            CompanionAppCatalog.PresetCustom,
            @"C:\Missing\MyEditor.exe",
            ".");

        Assert.Equal(CompanionAppCatalog.PresetCustom, state.Preset);
        Assert.False(state.LaunchOnWorkspaceOpen);
        Assert.Equal(@"C:\Missing\MyEditor.exe", state.Path);
        Assert.True(CompanionAppCatalog.ShouldShowPathWarning(state.Preset, state.Path));
    }

    [Fact]
    public void CreateStateFromPreset_None_ClearsCompanion()
    {
        var state = CompanionAppCatalog.CreateStateFromPreset(CompanionAppCatalog.PresetNone);

        Assert.False(state.LaunchOnWorkspaceOpen);
        Assert.Equal(string.Empty, state.Path);
        Assert.Equal(string.Empty, state.Arguments);
    }

    [Fact]
    public void ReconcileForForm_CatalogPreset_ReResolvesWhenInstalled()
    {
        if (!CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetVsCode))
        {
            return;
        }

        var state = CompanionAppCatalog.ReconcileForForm(
            CompanionAppCatalog.PresetVsCode,
            @"C:\Stale\Code.exe",
            "--old");

        Assert.Equal(CompanionAppCatalog.PresetVsCode, state.Preset);
        Assert.True(state.LaunchOnWorkspaceOpen);
        Assert.True(CompanionAppCatalog.TryResolveExecutablePath(state.Path, out _));
        Assert.Equal(".", state.Arguments);
    }

    public void Dispose()
    {
        CompanionAppCatalog.TryResolveExecutableOverride = null;
        CompanionAppPreference.ReadLastUsedOverride = null;
        CompanionAppPreference.WriteLastUsedOverride = null;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}

public sealed class ShortcutHealthTests : IDisposable
{
    private readonly string _root;

    public ShortcutHealthTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void WouldNeedRepair_ReturnsTrueWhenDirectoryMissingOnDisk()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Gone",
            Directory = Path.Combine(_root, "missing-folder"),
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        Assert.True(ShortcutHealth.WouldNeedRepair(shortcut));
        Assert.Equal(ShortcutGlyphs.IncidentTriangle, ShortcutHealth.GetListGlyph(shortcut));
        Assert.Contains("Folder not found", ShortcutHealth.BuildListSubtitle(shortcut), StringComparison.Ordinal);
    }

    [Fact]
    public void WouldNeedRepair_ReturnsFalseForHealthyShortcut()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Healthy",
            Directory = _root,
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        Assert.False(ShortcutHealth.WouldNeedRepair(shortcut));
        var glyph = ShortcutHealth.GetListGlyph(shortcut);
        Assert.False(string.IsNullOrWhiteSpace(glyph));
    }

    [Fact]
    public void GetListGlyph_UsesAdminIconWhenHealthyAndElevated()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Admin",
            Directory = _root,
            RunAsAdmin = true,
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        Assert.False(ShortcutHealth.WouldNeedRepair(shortcut));
        Assert.Equal(ShortcutGlyphs.AdminLaunch, ShortcutHealth.GetListGlyph(shortcut));
    }

    [Fact]
    public void BuildListSubtitle_WarnsWhenCompanionAppMissing()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Missing companion",
            Directory = _root,
            OpenCompanionAppOnLaunch = true,
            CompanionAppPath = @"C:\Missing\Code.exe",
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        Assert.False(ShortcutHealth.WouldNeedRepair(shortcut));
        Assert.Contains("Companion app missing", ShortcutHealth.BuildListSubtitle(shortcut), StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayHelpers_DoNotSynthesizeLegacyLaunchesIntoShortcut()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Legacy",
            Directory = _root,
            Command = "npm test",
            Terminal = "wt",
            Launches = [],
        };

        _ = ShortcutHealth.GetListGlyph(shortcut);
        _ = ShortcutHealth.BuildListSubtitle(shortcut);

        Assert.Empty(shortcut.Launches);
        Assert.False(ShortcutHealth.WouldNeedRepair(shortcut));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}

public sealed class TerminalLaunchGlyphsTests
{
    [Fact]
    public void GetForLaunch_UsesPowerShellProfileIconForPwshWhenAvailable()
    {
        WtProfilesService.InvalidateCache();
        var launch = new WorkspaceEntry { Terminal = "pwsh", IsEnabled = true };
        var icon = TerminalLaunchGlyphs.GetForLaunch(launch);

        Assert.False(string.IsNullOrWhiteSpace(icon));
        Assert.True(
            icon == ShortcutGlyphs.PowerShell
            || icon.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
            || icon.Contains("ProfileIcons", StringComparison.OrdinalIgnoreCase),
            $"Unexpected pwsh icon '{icon}'");
    }

    [Fact]
    public void GetForLaunch_UsesPenguinForUbuntuProfile()
    {
        var launch = new WorkspaceEntry { Terminal = "wt", WtProfile = "Ubuntu", IsEnabled = true };

        Assert.Equal("\U0001F427", TerminalLaunchGlyphs.GetForLaunch(launch));
    }

    [Fact]
    public void GetForLaunch_UsesConfiguredDefaultProfileIcon()
    {
        WtProfilesService.InvalidateCache();
        var launch = new WorkspaceEntry { Terminal = "default", IsEnabled = true };
        var icon = TerminalLaunchGlyphs.GetForLaunch(launch);

        Assert.False(string.IsNullOrWhiteSpace(icon));
        Assert.NotEqual(ShortcutGlyphs.IncidentTriangle, icon);
    }
}
