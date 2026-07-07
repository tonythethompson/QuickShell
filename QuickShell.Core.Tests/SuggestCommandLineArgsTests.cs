using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class SuggestCommandLineArgsTests
{
    [Fact]
    public void TryParse_RepeatedUsedFlags_PreservesCommandsWithCommas()
    {
        var ok = SuggestCommandLineArgs.TryParse(
            ["suggest", "--dir", @"C:\Projects\demo", "--used", @"git commit -m ""a, b""", "--used", "npm run dev", "--generation", "3"],
            out var directory,
            out var usedCommands,
            out var generation);

        Assert.True(ok);
        Assert.Equal(@"C:\Projects\demo", directory);
        Assert.Equal(2, usedCommands.Count);
        Assert.Equal(@"git commit -m ""a, b""", usedCommands[0]);
        Assert.Equal("npm run dev", usedCommands[1]);
        Assert.Equal(3, generation);
    }

    [Fact]
    public void TryParse_WithoutUsed_ReturnsEmptyList()
    {
        var ok = SuggestCommandLineArgs.TryParse(
            ["suggest", "--dir", @"C:\Projects\demo"],
            out _,
            out var usedCommands,
            out _);

        Assert.True(ok);
        Assert.Empty(usedCommands);
    }
}
