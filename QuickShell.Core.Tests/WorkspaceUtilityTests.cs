using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class GitRepoDiscoveryTests : IDisposable
{
    private readonly string _root;

    public GitRepoDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-git-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    public void Dispose()
    {
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
    [InlineData(8, 8)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void NormalizeCount_ClampsToRange(int input, int expected) =>
        Assert.Equal(expected, QuickShellRecentSettings.NormalizeCount(input));

    [Fact]
    public void TryParseCount_PrefersInvariantCultureDigits()
    {
        Assert.True(QuickShellRecentSettings.TryParseCount("12", out var parsed));
        Assert.Equal(12, parsed);
        Assert.Equal("12", QuickShellRecentSettings.FormatCount(12));
    }

    [Fact]
    public void GetRecentWorkspaces_RespectsMaxCount()
    {
        var shortcuts = Enumerable.Range(1, 12)
            .Select(index => new TerminalShortcut
            {
                Id = index.ToString(),
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

        var matches = GitRepoIndex.Search("alpha", [_root], saved);

        Assert.Empty(matches);

        matches = GitRepoIndex.Search("alpha", [_root], savedDirectories: null);

        Assert.Single(matches);
        Assert.Equal("alpha-app", matches[0].Name);
    }

    public void Dispose()
    {
        GitRepoIndex.Invalidate();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}

public sealed class CompanionAppTests : IDisposable
{
    private readonly string _root;

    public CompanionAppTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-companion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersVsCodeWhenDotVscodeExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);

        if (CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCode) is null)
        {
            Assert.Null(suggestion);
            return;
        }

        Assert.NotNull(suggestion);
        Assert.Equal(CompanionAppCatalog.PresetVsCode, suggestion!.PresetId);
        Assert.Equal(".", suggestion.Arguments);
        Assert.True(suggestion.EnableOnLaunch);
    }

    [Fact]
    public void TrySuggestFromDirectory_FallsThroughWhenHigherPriorityCompanionMissing()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".cursor"));
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));

        var cursorInstalled = CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetCursor) is not null;
        var vsCodeInstalled = CompanionAppCatalog.TryResolveExecutable(CompanionAppCatalog.PresetVsCode) is not null;
        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);

        if (!cursorInstalled && vsCodeInstalled)
        {
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetVsCode, suggestion!.PresetId);
            return;
        }

        if (cursorInstalled)
        {
            Assert.NotNull(suggestion);
            Assert.Equal(CompanionAppCatalog.PresetCursor, suggestion!.PresetId);
            return;
        }

        if (!vsCodeInstalled)
        {
            Assert.Null(suggestion);
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
        var values = document.RootElement
            .EnumerateArray()
            .Select(choice => choice.GetProperty("value").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(CompanionAppCatalog.PresetNone, values);
        Assert.Contains(CompanionAppCatalog.PresetCustom, values);

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
    public void GetContextMenuIcon_UsesCodeIconForCursor()
    {
        Assert.Equal(
            "\uE90F",
            CompanionAppCatalog.GetContextMenuIcon(
                @"C:\Users\me\AppData\Local\Programs\cursor\Cursor.exe"));
    }

    [Fact]
    public void GetContextMenuIcon_UsesOpenWithForUnknownCustomExe()
    {
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
            CompanionAppCatalog.PresetFork,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\Fork\Fork.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetRider,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\JetBrains\Rider\bin\rider64.exe"));
        Assert.Equal(
            CompanionAppCatalog.PresetIntelliJIdea,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\JetBrains\IntelliJ IDEA\bin\idea64.exe"));
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
    public void TrySuggestFromDirectory_PrefersObsidianWhenVaultMarkerExists()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".obsidian"));

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);

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

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);
        if (suggestion is null)
        {
            Assert.False(CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetFork)
                || CompanionAppCatalog.IsPresetInstalled(CompanionAppCatalog.PresetGitHubDesktop));
            return;
        }

        Assert.True(
            suggestion.PresetId is CompanionAppCatalog.PresetFork or CompanionAppCatalog.PresetGitHubDesktop);
    }

    [Fact]
    public void TrySuggestFromDirectory_PrefersVisualStudioWhenSolutionExists()
    {
        File.WriteAllText(Path.Combine(_root, "App.sln"), string.Empty);

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);
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

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);
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

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);
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

        var suggestion = CompanionAppDetection.TrySuggestFromDirectory(_root);
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
    public void NeedsRepair_ReturnsTrueWhenDirectoryMissingOnDisk()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Gone",
            Directory = Path.Combine(_root, "missing-folder"),
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        Assert.True(ShortcutHealth.NeedsRepair(shortcut));
        Assert.Equal(ShortcutGlyphs.IncidentTriangle, ShortcutHealth.GetListGlyph(shortcut));
        Assert.Contains("Folder not found", ShortcutHealth.BuildListSubtitle(shortcut), StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsRepair_ReturnsFalseForHealthyShortcut()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Healthy",
            Directory = _root,
            Launches = [new WorkspaceEntry { Label = "Main", IsEnabled = true }],
        };

        Assert.False(ShortcutHealth.NeedsRepair(shortcut));
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

        Assert.False(ShortcutHealth.NeedsRepair(shortcut));
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

        Assert.False(ShortcutHealth.NeedsRepair(shortcut));
        Assert.Contains("Companion app missing", ShortcutHealth.BuildListSubtitle(shortcut), StringComparison.Ordinal);
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
