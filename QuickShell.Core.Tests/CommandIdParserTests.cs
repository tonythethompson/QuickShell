using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CommandIdParserTests
{
    private readonly CommandIdParser _parser = new();

    [Fact]
    public void TryParse_settings_id_maps_to_OpenSettings()
    {
        Assert.True(_parser.TryParse(CommandDescriptor.Settings().Id, out var settings));
        Assert.Equal(CommandKind.OpenSettings, settings.Kind);
    }

    [Fact]
    public void TryParse_import_conflict_maps_to_ImportConflict()
    {
        Assert.True(_parser.TryParse(CommandDescriptor.ImportConflict().Id, out var conflict));
        Assert.Equal(CommandKind.ImportConflict, conflict.Kind);
    }

    [Fact]
    public void TryParse_pending_edit_maps_to_PendingShortcutEdit()
    {
        Assert.True(_parser.TryParse(CommandDescriptor.PendingShortcutEdit().Id, out var pending));
        Assert.Equal(CommandKind.PendingShortcutEdit, pending.Kind);
    }

    [Fact]
    public void TryParse_create_workspace()
    {
        Assert.True(_parser.TryParse(CommandDescriptor.CreateWorkspace().Id, out var descriptor));
        Assert.Equal(CommandKind.CreateWorkspace, descriptor.Kind);
    }

    [Fact]
    public void TryParse_discover_create_includes_directory()
    {
        var directory = @"C:\repos\sample";
        var commandId = CommandDescriptor.DiscoverCreate(directory).Id;

        Assert.True(_parser.TryParse(commandId, out var descriptor));
        Assert.Equal(CommandKind.DiscoverCreateWorkspace, descriptor.Kind);
        Assert.Equal(Path.GetFullPath(directory), descriptor.Directory);
    }

    [Fact]
    public void TryParse_discover_git_repos()
    {
        Assert.True(_parser.TryParse(CommandDescriptor.DiscoverGitRepos().Id, out var descriptor));
        Assert.Equal(CommandKind.DiscoverGitRepos, descriptor.Kind);
    }

    [Fact]
    public void TryParse_open_launch_before_open()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");
        var launchCommandId = CommandDescriptor.OpenLaunch(shortcutId, launchId).Id;

        Assert.True(_parser.TryParse(launchCommandId, out var launch));
        Assert.Equal(CommandKind.OpenLaunch, launch.Kind);
        Assert.Equal(shortcutId, launch.WorkspaceId);
        Assert.Equal(launchId, launch.LaunchId);

        var openCommandId = CommandDescriptor.OpenWorkspace(shortcutId).Id;
        Assert.True(_parser.TryParse(openCommandId, out var open));
        Assert.Equal(CommandKind.OpenWorkspace, open.Kind);
        Assert.Equal(shortcutId, open.WorkspaceId);
    }

    [Fact]
    public void TryParse_workspace_status()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(CommandDescriptor.WorkspaceStatus(shortcutId).Id, out var descriptor));
        Assert.Equal(CommandKind.WorkspaceStatus, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
    }

    [Fact]
    public void TryParse_worktree_branch_picker()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(CommandDescriptor.WorktreeBranchPicker(shortcutId).Id, out var descriptor));
        Assert.Equal(CommandKind.WorktreeBranchPicker, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
    }

    [Fact]
    public void TryParse_worktree_branch_select()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(CommandDescriptor.WorktreeBranchSelect(shortcutId, "feature/x").Id, out var descriptor));
        Assert.Equal(CommandKind.WorktreeBranchSelect, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
        Assert.Equal("feature/x", descriptor.Branch);
    }

    [Fact]
    public void TryParse_worktree_branch_clear()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(CommandDescriptor.WorktreeBranchClear(shortcutId).Id, out var descriptor));
        Assert.Equal(CommandKind.WorktreeBranchClear, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TryParse_open_launch_strips_variant_suffixes(bool runAsAdmin, bool runAsStandard)
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");

        Assert.True(_parser.TryParse(
            CommandDescriptor.OpenLaunch(shortcutId, launchId, runAsAdmin, runAsStandard).Id,
            out var launch));
        Assert.Equal(shortcutId, launch.WorkspaceId);
        Assert.Equal(launchId, launch.LaunchId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TryParse_open_strips_variant_suffixes(bool runAsAdmin, bool runAsStandard)
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(
            CommandDescriptor.OpenWorkspace(shortcutId, runAsAdmin, runAsStandard).Id,
            out var open));
        Assert.Equal(shortcutId, open.WorkspaceId);
    }

    [Fact]
    public void OpenLaunch_parser_does_not_match_workspace_only_id()
    {
        var openId = CommandDescriptor.OpenWorkspace(Guid.NewGuid().ToString("N")).Id;
        Assert.True(_parser.TryParse(openId, out var workspace));
        Assert.Equal(CommandKind.OpenWorkspace, workspace.Kind);
    }

    [Fact]
    public void TryParse_unknown_returns_false()
    {
        Assert.False(_parser.TryParse("com.other.extension.foo", out _));
        Assert.False(_parser.TryParse(string.Empty, out _));
    }
}
