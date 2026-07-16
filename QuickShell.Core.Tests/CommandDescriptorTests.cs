using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CommandDescriptorTests
{
    [Fact]
    public void OpenLaunch_BuildsExpectedId()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");

        var commandId = CommandDescriptor.OpenLaunch(shortcutId, launchId).Id;
        Assert.Contains(CommandDescriptor.LaunchSeparator, commandId);
        Assert.StartsWith(CommandDescriptor.OpenPrefix, commandId);
    }

    [Fact]
    public void DiscoverCreate_RoundTripsDirectoryPath()
    {
        var directory = @"A:\repos\QuickShell";
        var commandId = CommandDescriptor.DiscoverCreate(directory).Id;

        Assert.True(new CommandIdParser().TryParse(commandId, out var descriptor));
        Assert.Equal(Path.GetFullPath(directory), descriptor.Directory);
    }

    [Fact]
    public void WorktreeBranchIds_UseStablePrefixes()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.StartsWith(CommandDescriptor.WorktreeBranchPickerPrefix, CommandDescriptor.WorktreeBranchPicker(shortcutId).Id);
        Assert.StartsWith(CommandDescriptor.WorktreeBranchClearPrefix, CommandDescriptor.WorktreeBranchClear(shortcutId).Id);
        Assert.StartsWith(CommandDescriptor.WorktreeBranchSelectPrefix, CommandDescriptor.WorktreeBranchSelect(shortcutId, "main").Id);
    }

    [Fact]
    public void IsStableId_Accepts32CharacterHexString()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.True(CommandDescriptor.IsStableId(id));
    }

    [Fact]
    public void IsStableId_RejectsShortAndNonHexStrings()
    {
        Assert.False(CommandDescriptor.IsStableId("abc"));
        Assert.False(CommandDescriptor.IsStableId(new string('g', 32)));
    }
}
