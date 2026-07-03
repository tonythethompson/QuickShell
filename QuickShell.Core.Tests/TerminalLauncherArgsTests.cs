using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class TerminalLauncherArgsTests
{
    [Fact]
    public void EscapeCmd_DoublesEmbeddedQuotes()
    {
        Assert.Equal(@"foo""""bar", TerminalLauncherArgs.EscapeCmd(@"foo""bar"));
    }

    [Fact]
    public void EscapeWindowsTerminalArg_DoublesTrailingBackslashes()
    {
        Assert.Equal(@"C:\\", TerminalLauncherArgs.EscapeWindowsTerminalArg(@"C:\"));
        Assert.Equal(@"C:\Projects\\", TerminalLauncherArgs.EscapeWindowsTerminalArg(@"C:\Projects\"));
        Assert.Equal(@"has \"" inside", TerminalLauncherArgs.EscapeWindowsTerminalArg(@"has "" inside"));
    }

    [Fact]
    public void EscapeSingleQuotedPowerShell_DoublesSingleQuotes()
    {
        Assert.Equal("C:\\Users\\o''malley", TerminalLauncherArgs.EscapeSingleQuotedPowerShell(@"C:\Users\o'malley"));
    }

    [Fact]
    public void ToPowerShellArguments_IncludesSetLocationAndCommand()
    {
        var shortcut = new TerminalShortcut { Command = "npm start" };
        var args = TerminalLauncherArgs.ToPowerShellArguments(shortcut, @"C:\Projects\App");

        Assert.Contains("-NoExit", args, StringComparison.Ordinal);
        Assert.Contains(@"C:\Projects\App", args, StringComparison.Ordinal);
        Assert.Contains("npm start", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCmdArguments_QuotesDirectoryWithSpaces()
    {
        var shortcut = new TerminalShortcut
        {
            Directory = @"C:\My Projects\App",
            Command = "dir",
        };

        var args = TerminalLauncherArgs.BuildCmdArguments(shortcut);

        Assert.Equal("/k \"cd /d \"\"C:\\My Projects\\App\"\" && dir\"", args);
    }

    [Fact]
    public void BuildCmdArguments_OmitsCommandWhenEmpty()
    {
        var shortcut = new TerminalShortcut
        {
            Directory = @"C:\My Projects\App",
        };

        var args = TerminalLauncherArgs.BuildCmdArguments(shortcut);

        Assert.Equal("/k \"cd /d \"\"C:\\My Projects\\App\"\"\"", args);
    }

    [Fact]
    public void BuildWindowsTerminalCmdSuffix_OmitsTrailingAndWhenCommandEmpty()
    {
        var shortcut = new TerminalShortcut
        {
            Directory = @"C:\My Projects\App",
        };

        var args = TerminalLauncherArgs.BuildWindowsTerminalCmdSuffix(shortcut);

        Assert.Equal("cmd.exe /k \"cd /d \"\"C:\\My Projects\\App\"\"\"", args);
    }

    [Fact]
    public void ToWslArguments_IncludesDistroCdAndCommand()
    {
        var shortcut = new TerminalShortcut { Command = "ls -la" };
        var target = new LaunchTarget
        {
            Id = "wsl:Ubuntu",
            DisplayName = "Ubuntu",
            Kind = LaunchTargetKind.Wsl,
            ProfileOrDistro = "Ubuntu",
        };
        var location = new WslPathResolver.WslLocation
        {
            LinuxPath = "/home/user/project",
            Distro = "Ubuntu",
        };

        var args = TerminalLauncherArgs.ToWslArguments(shortcut, target, location);

        Assert.Contains("-d \"Ubuntu\"", args, StringComparison.Ordinal);
        Assert.Contains("/home/user/project", args, StringComparison.Ordinal);
        Assert.Contains("bash -lc", args, StringComparison.Ordinal);
    }

    [Fact]
    public void ToWslArguments_PrefersDistroFromWtCommandLineOverProfileName()
    {
        var shortcut = new TerminalShortcut { Directory = @"C:\Projects\App" };
        var target = new LaunchTarget
        {
            Id = "wt:dev-shell",
            DisplayName = "Dev Shell",
            Kind = LaunchTargetKind.WindowsTerminal,
            ProfileOrDistro = "Dev Shell",
            WtCommandLine = "wsl.exe -d Ubuntu",
        };
        var location = new WslPathResolver.WslLocation
        {
            LinuxPath = "/mnt/c/Projects/App",
        };

        var args = TerminalLauncherArgs.ToWslArguments(shortcut, target, location);

        Assert.Contains("-d \"Ubuntu\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain("Dev Shell", args, StringComparison.Ordinal);
    }

    [Fact]
    public void ToWslArguments_ParsesLongDistributionFlagFromWtCommandLine()
    {
        var shortcut = new TerminalShortcut { Directory = @"C:\Projects\App" };
        var target = new LaunchTarget
        {
            Id = "wt:dev-shell",
            DisplayName = "Dev Shell",
            Kind = LaunchTargetKind.WindowsTerminal,
            ProfileOrDistro = "Dev Shell",
            WtCommandLine = "wsl.exe --distribution Debian",
        };
        var location = new WslPathResolver.WslLocation
        {
            LinuxPath = "/mnt/c/Projects/App",
        };

        var args = TerminalLauncherArgs.ToWslArguments(shortcut, target, location);

        Assert.Contains("-d \"Debian\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain("Dev Shell", args, StringComparison.Ordinal);
    }
}
