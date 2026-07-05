using System.Diagnostics;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Groups every test class that mutates the process-wide static
/// <see cref="TerminalLauncher.StartProcessOverride"/> seam so xUnit never
/// runs two of them concurrently — by default, each test *class* is its own
/// parallel collection, and two classes racing to set/reset the same static
/// field can capture (or fail to reset) each other's override.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TerminalLauncherOverrideCollection
{
    public const string Name = "TerminalLauncher.StartProcessOverride";
}

/// <summary>
/// End-to-end coverage for <see cref="TerminalLauncher.Open"/>: the full
/// resolve-target -> build-ProcessStartInfo -> (would-be) launch pipeline,
/// using <see cref="TerminalLauncher.StartProcessOverride"/> to capture the
/// resulting <see cref="ProcessStartInfo"/> instead of actually spawning a
/// process. <see cref="TerminalLauncherArgsTests"/> covers the string-builder
/// helpers in isolation; this file covers the dispatch + elevation logic that
/// wraps them, which is where the "silently resets/falls back" class of bugs
/// tends to live.
///
/// Scenarios are deliberately restricted to targets that resolve
/// deterministically regardless of what's actually installed on the machine
/// running the tests (cmd.exe/powershell.exe are always under
/// %SystemRoot%\System32 on any Windows box; a "Windows Terminal, default
/// profile" target is synthesized from a hardcoded id without touching live
/// executable discovery). WSL/pwsh/custom-profile argument construction is
/// covered at the <see cref="TerminalLauncherArgs"/>/<see cref="WslPathResolver"/>
/// level instead, since resolving those end-to-end depends on what's actually
/// installed on the CI runner.
/// </summary>
[Collection(TerminalLauncherOverrideCollection.Name)]
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

        var captured = false;
        TerminalLauncher.StartProcessOverride = _ => { captured = true; return true; };
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => TerminalLauncher.Open(shortcut, "wt", TerminalHostIds.DefaultProfile));
            Assert.False(captured, "No process should be started when the directory doesn't exist.");
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }
    }

    [Fact]
    public void Open_ShortcutRunAsAdmin_SetsRunasVerb()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "Elevated", Directory = directory.Path, RunAsAdmin = true };

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, "wt", TerminalHostIds.DefaultProfile));

        Assert.Equal("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_RunAsAdminParameter_SetsRunasVerb_EvenWhenShortcutFlagIsFalse()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "NotFlagged", Directory = directory.Path, RunAsAdmin = false };

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, "wt", TerminalHostIds.DefaultProfile, runAsAdmin: true));

        Assert.Equal("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_RunAsStandard_SuppressesElevation_EvenWhenShortcutRequestsIt()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "ForcedStandard", Directory = directory.Path, RunAsAdmin = true };

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, "wt", TerminalHostIds.DefaultProfile, runAsStandard: true));

        Assert.NotEqual("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_NonElevatedShortcut_DoesNotSetRunasVerb()
    {
        using var directory = new TempDataDirectory();
        var shortcut = new TerminalShortcut { Name = "Plain", Directory = directory.Path };

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, "wt", TerminalHostIds.DefaultProfile));

        Assert.NotEqual("runas", startInfo.Verb);
    }

    [Fact]
    public void Open_WindowsTerminalDefaultProfile_PassesDirectoryAndCommandWithSpaces()
    {
        using var directory = new TempDataDirectory("My Projects App");
        var shortcut = new TerminalShortcut { Name = "Spaced", Directory = directory.Path, Command = "npm run dev" };

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, TerminalHostIds.WindowsTerminal, TerminalHostIds.DefaultProfile));

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

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, TerminalHostIds.WindowsConsoleHost, "cmd"));

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

        var startInfo = Capture(() => TerminalLauncher.Open(shortcut, TerminalHostIds.WindowsConsoleHost, "powershell"));

        Assert.Equal("powershell.exe", startInfo.FileName);
        Assert.Contains("Set-Location -LiteralPath", startInfo.Arguments, StringComparison.Ordinal);
        // A literal single quote in the path must be doubled for PowerShell's single-quoted string.
        Assert.Contains("O''Malley''s Project", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("git status", startInfo.Arguments, StringComparison.Ordinal);
    }

    private static ProcessStartInfo Capture(Action open)
    {
        ProcessStartInfo? captured = null;
        TerminalLauncher.StartProcessOverride = info => { captured = info; return true; };
        try
        {
            open();
        }
        finally
        {
            TerminalLauncher.StartProcessOverride = null;
        }

        Assert.NotNull(captured);
        return captured;
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
