using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WslPathResolverTests
{
    [Theory]
    [InlineData(@"A:\Trackdub\frontend", "/mnt/a/Trackdub/frontend")]
    [InlineData(@"C:\Projects\App", "/mnt/c/Projects/App")]
    [InlineData(@"D:\", "/mnt/d")]
    [InlineData(@"E:/foo/bar/", "/mnt/e/foo/bar")]
    public void ConvertWindowsPathToLinuxPath_MapsDrivePathsToWslMount(string windowsPath, string expectedLinuxPath)
    {
        Assert.Equal(expectedLinuxPath, WslPathResolver.ConvertWindowsPathToLinuxPath(windowsPath));
    }

    [Fact]
    public void CreateLocationFromWindowsDirectory_ConvertsWindowsWorkspaceForUbuntuProfile()
    {
        var target = new LaunchTarget
        {
            Id = "wt:Ubuntu",
            DisplayName = "Ubuntu",
            Kind = LaunchTargetKind.Wsl,
            ProfileOrDistro = "Ubuntu",
            WtCommandLine = "wsl.exe -d Ubuntu",
        };

        var location = WslPathResolver.CreateLocationFromWindowsDirectory(@"A:\Trackdub\frontend", target);

        Assert.Equal("/mnt/a/Trackdub/frontend", location.LinuxPath);
    }

    [Fact]
    public void ToWslArguments_UsesConvertedLinuxPathForWindowsWorkspace()
    {
        var shortcut = new TerminalShortcut
        {
            Directory = @"A:\Trackdub\frontend",
            Command = "npm run dev",
        };
        var target = new LaunchTarget
        {
            Id = "wt:Ubuntu",
            DisplayName = "Ubuntu",
            Kind = LaunchTargetKind.WindowsTerminal,
            ProfileOrDistro = "Ubuntu",
            WtCommandLine = "wsl.exe -d Ubuntu",
        };
        var location = WslPathResolver.CreateLocationFromWindowsDirectory(shortcut.Directory, target);

        var args = TerminalLauncherArgs.ToWslArguments(shortcut, target, location);

        Assert.Contains("--cd \"/mnt/a/Trackdub/frontend\"", args, StringComparison.Ordinal);
        Assert.Contains("bash -lc", args, StringComparison.Ordinal);
        Assert.Contains("npm run dev", args, StringComparison.Ordinal);
    }
}
