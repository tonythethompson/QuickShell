using QuickShell.Abstractions;
using System.Diagnostics;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// End-to-end coverage for <see cref="TerminalLauncher.Open"/> using a capturing
/// <see cref="FakeProcessStarter"/> instead of process-wide static overrides.
/// </summary>
public sealed class TerminalLauncherTests
{
    [Fact]
    public void Open_ThrowsBeforeLaunching_WhenDirectoryDoesNotExist()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Missing",
            Directory = @"C:\this-directory-should-not-exist-quickshell-test",
        };

        var starter = new FakeProcessStarter { Succeed = true };
        var launcher = new TerminalLauncher(starter, new TerminalCatalog(new WtProfilesService()));

        Assert.Throws<DirectoryNotFoundException>(() =>
            launcher.Open(shortcut, "wt", TerminalHostIds.DefaultProfile));
        Assert.Empty(starter.Started);
    }

    [Fact]
    public void Open_ShortcutRunAsAdmin_SetsRunasVerb()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "Elevated", Directory = directory.Path, RunAsAdmin = true };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, "wt", TerminalHostIds.DefaultProfile));

        Assert.Equal("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_RunAsAdminParameter_SetsRunasVerb_EvenWhenShortcutFlagIsFalse()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "NotFlagged", Directory = directory.Path, RunAsAdmin = false };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, "wt", TerminalHostIds.DefaultProfile, runAsAdmin: true));

        Assert.Equal("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_RunAsStandard_SuppressesElevation_EvenWhenShortcutRequestsIt()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "ForcedStandard", Directory = directory.Path, RunAsAdmin = true };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, "wt", TerminalHostIds.DefaultProfile, runAsStandard: true));

        Assert.NotEqual("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_NonElevatedShortcut_DoesNotSetRunasVerb()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "Plain", Directory = directory.Path };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, "wt", TerminalHostIds.DefaultProfile));

        Assert.NotEqual("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_WindowsTerminalDefaultProfile_PassesDirectoryAndCommandWithSpaces()
    {
        using var directory = new TempDataDirectory("My Projects App");
        var shortcut = new TerminalShortcut { Name = "Spaced", Directory = directory.Path, Command = "npm run dev" };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, TerminalHostIds.WindowsTerminal, TerminalHostIds.DefaultProfile));

        Assert.Equal("wt.exe", startInfo.FileName);
        Assert.Contains($"-d \"{directory.Path}\"", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("npm run dev", startInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_Cmd_QuotesDirectoryAndDoublesEmbeddedQuotesInCommand()
    {
        using var directory = new TempDataDirectory("My Projects");
        var shortcut = new TerminalShortcut
        {
            Name = "Cmd",
            Directory = directory.Path,
            Command = "echo \"hello world\"",
        };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, TerminalHostIds.WindowsConsoleHost, "cmd"));

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Contains($"cd /d \"\"{directory.Path}\"\"", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("echo \"\"hello world\"\"", startInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_PowerShell_UsesSetLocationLiteralPathAndRunsCommand()
    {
        using var directory = new TempDataDirectory("O'Malley's Project");
        var shortcut = new TerminalShortcut
        {
            Name = "Ps",
            Directory = directory.Path,
            Command = "git status",
        };

        var startInfo = Capture(shortcut, (launcher, s) =>
            launcher.Open(s, TerminalHostIds.WindowsConsoleHost, "powershell"));

        Assert.Equal("powershell.exe", startInfo.FileName);
        Assert.Contains("Set-Location -LiteralPath", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("O''Malley''s Project", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("git status", startInfo.Arguments, StringComparison.Ordinal);
    }

    private static ProcessStartInfo Capture(
        TerminalShortcut shortcut,
        Action<TerminalLauncher, TerminalShortcut> open)
    {
        var starter = new FakeProcessStarter { Succeed = true };
        var launcher = new TerminalLauncher(starter, new TerminalCatalog(new WtProfilesService()));
        open(launcher, shortcut);
        Assert.Single(starter.Started);
        return starter.Started[0];
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory(string? leafName = null)
        {
            var leaf = string.IsNullOrWhiteSpace(leafName) ? Guid.NewGuid().ToString("N") : leafName;
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickshell-tests", Guid.NewGuid().ToString("N"), leaf);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                var root = System.IO.Path.GetDirectoryName(Path)!;
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
