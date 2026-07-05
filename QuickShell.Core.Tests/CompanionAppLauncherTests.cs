using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Covers <see cref="CompanionAppLauncher.ExpandArguments"/> (the "." /
/// "{folder}" / "{solution}" placeholder substitution used for companion
/// app arguments) and the validation error paths of <see cref="CompanionAppLauncher.TryLaunch"/>.
/// The actual successful-launch path is intentionally not covered here —
/// unlike <see cref="TerminalLauncher"/>, <see cref="CompanionAppLauncher"/>
/// has no process-start override seam, so exercising it would spawn a real
/// process/window during test runs.
/// </summary>
public sealed class CompanionAppLauncherTests
{
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

        var success = CompanionAppLauncher.TryLaunch(shortcut, onDemand: true, out var error);

        Assert.False(success);
        Assert.Contains("No companion app is configured", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLaunch_AutoLaunchNotConfigured_ReturnsTrueWithoutError()
    {
        // Auto-launch (onDemand: false) with the opt-in flag off is a no-op, not an error.
        var shortcut = new TerminalShortcut { Name = "NotOptedIn", Directory = Environment.CurrentDirectory };

        var success = CompanionAppLauncher.TryLaunch(shortcut, onDemand: false, out var error);

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

        var success = CompanionAppLauncher.TryLaunch(shortcut, onDemand: true, out var error);

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

        var success = CompanionAppLauncher.TryLaunch(shortcut, onDemand: true, out var error);

        Assert.False(success);
        Assert.Contains("folder not found", error, StringComparison.OrdinalIgnoreCase);
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
