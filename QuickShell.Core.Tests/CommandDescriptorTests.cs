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

    [Fact]
    public void TryDecodeLegacyNameKey_RoundTripsNameAndRejectsStableId()
    {
        var name = "legacy shortcut name";
        var encoded = CommandDescriptor.EncodeNameKey(name);

        Assert.True(CommandDescriptor.TryDecodeLegacyNameKey(encoded, out var decoded));
        Assert.Equal(name, decoded);

        var stableId = Guid.NewGuid().ToString("N");
        Assert.False(CommandDescriptor.TryDecodeLegacyNameKey(stableId, out _));
    }

    [Fact]
    public void OpenWorkspace_BothVariantFlagsSet_PrefersAdminSuffix()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var commandId = CommandDescriptor.OpenWorkspace(shortcutId, runAsAdmin: true, runAsStandard: true).Id;
        Assert.EndsWith(".admin", commandId);
    }
}
