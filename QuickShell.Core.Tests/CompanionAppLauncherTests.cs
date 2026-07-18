using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Covers <see cref="CompanionAppLauncher.ExpandArguments"/> and instance launch paths
/// via <see cref="FakeProcessStarter"/>.
/// </summary>
[Collection(TerminalLauncherOverrideIsolation.Name)]
public sealed class CompanionAppLauncherTests : IDisposable
{
    public CompanionAppLauncherTests()
    {
        CompanionAppPreference.ReadLastUsedOverride = null;
        CompanionAppPreference.WriteLastUsedOverride = null;
    }

    public void Dispose()
    {
        CompanionAppPreference.ReadLastUsedOverride = null;
        CompanionAppPreference.WriteLastUsedOverride = null;
    }

    [Fact]
    public void ExpandArguments_NullOrWhitespace_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, CompanionAppLauncher.ExpandArguments(null, @"C:\Projects\App"));
        Assert.Equal(string.Empty, CompanionAppLauncher.ExpandArguments("   ", @"C:\Projects\App"));
    }

    [Fact]
    public void ExpandArguments_DotShorthand_QuotesDirectoryWithSpaces()
    {
        var result = CompanionAppLauncher.ExpandArguments(".", @"C:\My Projects\App");

        Assert.Equal("\"C:\\My Projects\\App\"", result);
    }

    [Fact]
    public void ExpandArguments_DotShorthand_LeavesDirectoryWithoutSpacesUnquoted()
    {
        var result = CompanionAppLauncher.ExpandArguments(".", @"C:\Projects\App");

        Assert.Equal(@"C:\Projects\App", result);
    }

    [Fact]
    public void ExpandArguments_FolderPlaceholder_ExpandsAndQuotesWhenNeeded()
    {
        var result = CompanionAppLauncher.ExpandArguments("--project {folder} --verbose", @"C:\My Projects\App");

        Assert.Equal("--project \"C:\\My Projects\\App\" --verbose", result);
    }

    [Fact]
    public void ExpandArguments_FolderPlaceholder_IsCaseInsensitive()
    {
        var result = CompanionAppLauncher.ExpandArguments("open {FOLDER}", @"C:\Projects\App");

        Assert.Equal(@"open C:\Projects\App", result);
    }

    [Fact]
    public void ExpandArguments_SolutionPlaceholder_ExpandsToSolutionFileWhenPresent()
    {
        using var directory = new TempDataDirectory();
        var solutionPath = Path.Combine(directory.Path, "App.sln");
        File.WriteAllText(solutionPath, "Microsoft Visual Studio Solution File");

        var result = CompanionAppLauncher.ExpandArguments("{solution}", directory.Path);

        Assert.Equal(QuoteIfNeeded(solutionPath), result);
    }

    [Fact]
    public void ExpandArguments_SolutionPlaceholder_FallsBackToWorkspaceDirectoryWhenNoSolutionExists()
    {
        using var directory = new TempDataDirectory();

        var result = CompanionAppLauncher.ExpandArguments("{solution}", directory.Path);

        Assert.Equal(QuoteIfNeeded(directory.Path), result);
    }

    private static string QuoteIfNeeded(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    [Fact]
    public void TryLaunch_OnDemand_NoCompanionConfigured_ReturnsActionableError()
    {
        var shortcut = new TerminalShortcut { Name = "NoCompanion", Directory = Environment.CurrentDirectory };
        var launcher = new CompanionAppLauncher(new FakeProcessStarter());

        var success = launcher.TryLaunch(shortcut, onDemand: true, out var error);

        Assert.False(success);
        Assert.Contains("No companion app is configured", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLaunch_AutoLaunchNotConfigured_ReturnsTrueWithoutError()
    {
        var shortcut = new TerminalShortcut { Name = "NotOptedIn", Directory = Environment.CurrentDirectory };
        var launcher = new CompanionAppLauncher(new FakeProcessStarter());

        var success = launcher.TryLaunch(shortcut, onDemand: false, out var error);

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public void TryLaunch_UnresolvableCompanionPath_ReturnsActionableError()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "BadCompanion",
            Directory = Environment.CurrentDirectory,
            CompanionAppPath = @"C:\this-executable-should-not-exist-quickshell-test.exe",
        };
        var launcher = new CompanionAppLauncher(new FakeProcessStarter());

        var success = launcher.TryLaunch(shortcut, onDemand: true, out var error);

        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLaunch_MissingWorkspaceDirectory_ReturnsActionableError()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "MissingDir",
            Directory = @"C:\this-directory-should-not-exist-quickshell-test",
            CompanionAppPath = "explorer.exe",
        };
        var launcher = new CompanionAppLauncher(new FakeProcessStarter());

        var success = launcher.TryLaunch(shortcut, onDemand: true, out var error);

        Assert.False(success);
        Assert.Contains("folder not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLaunch_Auto_LaunchesOnlyOpenOnLaunchEntries()
    {
        CompanionAppPreference.WriteLastUsedOverride = _ => { };
        var starter = new FakeProcessStarter { Succeed = true };
        var launcher = new CompanionAppLauncher(starter);

        var shortcut = new TerminalShortcut
        {
            Name = "Multi",
            Directory = Environment.CurrentDirectory,
            CompanionApps =
            [
                new CompanionAppEntry
                {
                    Id = "1",
                    Path = "explorer.exe",
                    Arguments = "{folder}",
                    OpenOnLaunch = true,
                    Order = 0,
                },
                new CompanionAppEntry
                {
                    Id = "2",
                    Path = "notepad.exe",
                    Arguments = string.Empty,
                    OpenOnLaunch = false,
                    Order = 1,
                },
                new CompanionAppEntry
                {
                    Id = "3",
                    Path = "cmd.exe",
                    Arguments = "/c echo hi",
                    OpenOnLaunch = true,
                    Order = 2,
                },
            ],
        };

        var result = launcher.Launch(shortcut, onDemand: false);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(2, launcher.LastLaunchCount);
        Assert.Equal(["explorer.exe", "cmd.exe"], result.StartedExecutables);
        Assert.Equal(2, starter.Started.Count);
        Assert.Contains(starter.Started, item => item.FileName.Contains("explorer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(starter.Started, item => item.FileName.Contains("cmd", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(starter.Started, item => item.FileName.Contains("notepad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryLaunch_OnDemand_LaunchesAllConfiguredEntries()
    {
        CompanionAppPreference.WriteLastUsedOverride = _ => { };
        var starter = new FakeProcessStarter { Succeed = true };
        var launcher = new CompanionAppLauncher(starter);

        var shortcut = new TerminalShortcut
        {
            Name = "OnDemandMulti",
            Directory = Environment.CurrentDirectory,
            CompanionApps =
            [
                new CompanionAppEntry
                {
                    Id = "1",
                    Path = "explorer.exe",
                    OpenOnLaunch = true,
                    Order = 0,
                },
                new CompanionAppEntry
                {
                    Id = "2",
                    Path = "notepad.exe",
                    OpenOnLaunch = false,
                    Order = 1,
                },
            ],
        };

        var success = launcher.TryLaunch(shortcut, onDemand: true, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(2, launcher.LastLaunchCount);
        Assert.Equal(2, starter.Started.Count);
    }

    [Fact]
    public void ShouldLaunchOnWorkspaceOpen_TrueWhenAnyEntryOptsIn()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Any",
            Directory = Environment.CurrentDirectory,
            CompanionApps =
            [
                new CompanionAppEntry { Id = "1", Path = "explorer.exe", OpenOnLaunch = false, Order = 0 },
                new CompanionAppEntry { Id = "2", Path = "notepad.exe", OpenOnLaunch = true, Order = 1 },
            ],
        };
        var launcher = new CompanionAppLauncher(new FakeProcessStarter());

        Assert.True(launcher.ShouldLaunchOnWorkspaceOpen(shortcut));
        Assert.True(launcher.IsConfigured(shortcut));
    }

    [Fact]
    public void BuildDisplaySummary_FormatsSingleAndMultiple()
    {
        var launcher = new CompanionAppLauncher(new FakeProcessStarter());
        var single = new TerminalShortcut
        {
            CompanionApps =
            [
                new CompanionAppEntry { Path = @"C:\Apps\Code.exe", Order = 0 },
            ],
        };
        Assert.False(string.IsNullOrWhiteSpace(launcher.BuildDisplaySummary(single)));

        var multi = new TerminalShortcut
        {
            CompanionApps =
            [
                new CompanionAppEntry { Path = @"C:\Apps\Code.exe", Order = 0 },
                new CompanionAppEntry { Path = @"C:\Apps\Fork.exe", Order = 1 },
                new CompanionAppEntry { Path = @"C:\Apps\Notes.exe", Order = 2 },
            ],
        };
        Assert.Equal("3 companions", launcher.BuildDisplaySummary(multi));
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickshell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
