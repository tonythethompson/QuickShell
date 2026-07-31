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

    [Theory]
    [InlineData(@"\\wsl$\Ubuntu\home\user\project", "Ubuntu", "/home/user/project")]
    [InlineData(@"\\wsl.localhost\Debian\home\user\project", "Debian", "/home/user/project")]
    [InlineData(@"\\wsl$\Ubuntu-20.04\home\dev", "Ubuntu-20.04", "/home/dev")]
    [InlineData(@"\\wsl.localhost\Alpine\root\workspace\nested\path", "Alpine", "/root/workspace/nested/path")]
    public void TryParse_UncPath_ReturnsValidLocation(string uncPath, string expectedDistro, string expectedLinuxPath)
    {
        var result = WslPathResolver.TryParse(uncPath, out var location);

        Assert.True(result);
        Assert.NotNull(location);
        Assert.Equal(expectedDistro, location.Distro);
        Assert.Equal(expectedLinuxPath, location.LinuxPath);
        Assert.Equal(uncPath, location.UncPath);
    }

    [Theory]
    [InlineData(@"\\wsl$\Ubuntu\\home\\user", "Ubuntu", "/home/user")]
    [InlineData(@"\\wsl.localhost\Debian\\\\home", "Debian", "/home")]
    public void TryParse_UncPath_WithRepeatedSeparators_SkipsEmptyParts(string uncPath, string expectedDistro, string expectedLinuxPath)
    {
        var result = WslPathResolver.TryParse(uncPath, out var location);

        Assert.True(result);
        Assert.NotNull(location);
        Assert.Equal(expectedDistro, location.Distro);
        Assert.Equal(expectedLinuxPath, location.LinuxPath);
    }

    [Theory]
    [InlineData(@"\\wsl$\Ubuntu\home\user\", "Ubuntu", "/home/user")]
    [InlineData(@"\\wsl.localhost\Debian\home\", "Debian", "/home")]
    public void TryParse_UncPath_WithTrailingSeparator_IgnoresTrailingEmpty(string uncPath, string expectedDistro, string expectedLinuxPath)
    {
        var result = WslPathResolver.TryParse(uncPath, out var location);

        Assert.True(result);
        Assert.NotNull(location);
        Assert.Equal(expectedDistro, location.Distro);
        Assert.Equal(expectedLinuxPath, location.LinuxPath);
    }

    [Theory]
    [InlineData(@"\\wsl$\")]
    [InlineData(@"\\wsl$\\")]
    [InlineData(@"\\wsl.localhost\")]
    [InlineData(@"\\wsl.localhost\\")]
    public void TryParse_UncPath_MissingDistro_ReturnsFalse(string uncPath)
    {
        var result = WslPathResolver.TryParse(uncPath, out var location);

        Assert.False(result);
    }

    [Theory]
    [InlineData(@"\\wsl$\Ubuntu")]
    [InlineData(@"\\wsl.localhost\Debian")]
    [InlineData(@"\\wsl$\Ubuntu\")]
    public void TryParse_UncPath_OnlyDistroNoLinuxPath_ReturnsFalse(string uncPath)
    {
        var result = WslPathResolver.TryParse(uncPath, out var location);

        Assert.False(result);
    }
}
