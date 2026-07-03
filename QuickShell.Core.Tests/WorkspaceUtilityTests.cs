using QuickShell.Models;
using QuickShell.Services;

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
    public void TryDetectDevLaunchCommand_ReturnsPackageManagerCommand()
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
        Assert.Equal("pnpm dev", DevServerUrlDetection.FormatPackageScriptCommand(_root, "dev"));
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

    [Fact]
    public void ApplyDirectoryHints_UpdatesExistingBlankLaunchEntry()
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
                    Id = "launch-1",
                    Label = "Main",
                    Terminal = "default",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        });

        Assert.Equal("npm run dev", seed.Command);
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
    public void ExpandArguments_ReplacesSolutionToken()
    {
        var directory = Path.Combine(_root, "sample app");
        Directory.CreateDirectory(directory);
        var solutionPath = Path.Combine(directory, "Sample App.sln");
        File.WriteAllText(solutionPath, string.Empty);

        Assert.Equal(
            $"\"{solutionPath}\"",
            CompanionAppLauncher.ExpandArguments("{solution}", directory));
        Assert.Equal(
            Path.Combine(_root, "no-solution"),
            CompanionAppLauncher.ExpandArguments("{solution}", Path.Combine(_root, "no-solution")));
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
            CompanionAppCatalog.PresetCustom,
            CompanionAppCatalog.InferPresetFromPath(@"C:\Apps\MyEditor.exe"));
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
