using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutCommandIdsTests
{
    [Fact]
    public void OpenLaunch_BuildsExpectedId()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");

        var commandId = ShortcutCommandIds.OpenLaunch(shortcutId, launchId);
        Assert.Contains(QuickShellDeepLinkIds.LaunchSeparator, commandId);
        Assert.StartsWith(QuickShellDeepLinkIds.OpenPrefix, commandId);
    }

    [Fact]
    public void DiscoverCreate_RoundTripsDirectoryPath()
    {
        var directory = @"A:\repos\QuickShell";
        var commandId = ShortcutCommandIds.DiscoverCreate(directory);
        Assert.True(CommandIdParser.TryDecodeDiscoverCreateDirectory(commandId, out var parsedDirectory));
        Assert.Equal(Path.GetFullPath(directory), parsedDirectory);
    }

    [Fact]
    public void WorktreeBranchIds_UseStablePrefixes()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.StartsWith(QuickShellDeepLinkIds.WorktreeBranchPickerPrefix, ShortcutCommandIds.WorktreeBranchPicker(shortcutId));
        Assert.StartsWith(QuickShellDeepLinkIds.WorktreeBranchClearPrefix, ShortcutCommandIds.WorktreeBranchClear(shortcutId));
        Assert.StartsWith(QuickShellDeepLinkIds.WorktreeBranchSelectPrefix, ShortcutCommandIds.WorktreeBranchSelect(shortcutId, "main"));
    }
}
