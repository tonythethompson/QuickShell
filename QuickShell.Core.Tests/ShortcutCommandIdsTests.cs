using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutCommandIdsTests
{
    [Fact]
    public void OpenLaunch_RoundTripsStableShortcutAndLaunchIds()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");

        var commandId = ShortcutCommandIds.OpenLaunch(shortcutId, launchId);
        var parsed = ShortcutCommandIds.TryParseOpenLaunch(commandId, out var parsedShortcutId, out var parsedLaunchId);

        Assert.True(parsed);
        Assert.Equal(shortcutId, parsedShortcutId);
        Assert.Equal(launchId, parsedLaunchId);
    }

    [Fact]
    public void TryParseOpenLaunch_RejectsWorkspaceOnlyOpenCommand()
    {
        var commandId = ShortcutCommandIds.Open(Guid.NewGuid().ToString("N"));

        var parsed = ShortcutCommandIds.TryParseOpenLaunch(commandId, out _, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData(".admin")]
    [InlineData(".standard")]
    public void TryParseOpenLaunch_StripsLaunchVariantSuffixes(string suffix)
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");

        var parsed = ShortcutCommandIds.TryParseOpenLaunch(
            ShortcutCommandIds.OpenLaunch(shortcutId, launchId) + suffix,
            out var parsedShortcutId,
            out var parsedLaunchId);

        Assert.True(parsed);
        Assert.Equal(shortcutId, parsedShortcutId);
        Assert.Equal(launchId, parsedLaunchId);
    }

    [Theory]
    [InlineData(".admin")]
    [InlineData(".standard")]
    public void TryParseOpen_StripsWorkspaceVariantSuffixes(string suffix)
    {
        var shortcutId = Guid.NewGuid().ToString("N");

        var parsed = ShortcutCommandIds.TryParseOpen(
            ShortcutCommandIds.Open(shortcutId) + suffix,
            out var parsedShortcutId);

        Assert.True(parsed);
        Assert.Equal(shortcutId, parsedShortcutId);
    }
}
