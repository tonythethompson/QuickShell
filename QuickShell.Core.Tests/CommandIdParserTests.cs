using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CommandIdParserTests
{
    private readonly CommandIdParser _parser = new();

    [Fact]
    public void TryParse_settings_id_maps_to_OpenSettings()
    {
        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.Settings, out var settings));
        Assert.Equal(CommandKind.OpenSettings, settings.Kind);
    }

    [Fact]
    public void TryParse_import_conflict_maps_to_ImportConflict()
    {
        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.ImportConflict, out var conflict));
        Assert.Equal(CommandKind.ImportConflict, conflict.Kind);
    }

    [Fact]
    public void TryParse_pending_edit_maps_to_PendingShortcutEdit()
    {
        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.PendingShortcutEdit, out var pending));
        Assert.Equal(CommandKind.PendingShortcutEdit, pending.Kind);
    }

    [Fact]
    public void TryParse_create_workspace()
    {
        Assert.True(_parser.TryParse(ShortcutCommandIds.CreateShortcut, out var descriptor));
        Assert.Equal(CommandKind.CreateWorkspace, descriptor.Kind);
    }

    [Fact]
    public void TryParse_discover_create_includes_directory()
    {
        var directory = @"C:\repos\sample";
        var commandId = ShortcutCommandIds.DiscoverCreate(directory);

        Assert.True(_parser.TryParse(commandId, out var descriptor));
        Assert.Equal(CommandKind.DiscoverCreateWorkspace, descriptor.Kind);
        Assert.Equal(Path.GetFullPath(directory), descriptor.Directory);
    }

    [Fact]
    public void TryParse_discover_git_repos()
    {
        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.DiscoverGitRepos, out var descriptor));
        Assert.Equal(CommandKind.DiscoverGitRepos, descriptor.Kind);
    }

    [Fact]
    public void TryParse_open_launch_before_open()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");
        var launchCommandId = ShortcutCommandIds.OpenLaunch(shortcutId, launchId);

        Assert.True(_parser.TryParse(launchCommandId, out var launch));
        Assert.Equal(CommandKind.OpenLaunch, launch.Kind);
        Assert.Equal(shortcutId, launch.WorkspaceId);
        Assert.Equal(launchId, launch.LaunchId);

        var openCommandId = ShortcutCommandIds.Open(shortcutId);
        Assert.True(_parser.TryParse(openCommandId, out var open));
        Assert.Equal(CommandKind.OpenWorkspace, open.Kind);
        Assert.Equal(shortcutId, open.WorkspaceId);
    }

    [Fact]
    public void TryParse_workspace_status()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(ShortcutCommandIds.WorkspaceStatus(shortcutId), out var descriptor));
        Assert.Equal(CommandKind.WorkspaceStatus, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
    }

    [Fact]
    public void TryParse_worktree_branch_picker()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(ShortcutCommandIds.WorktreeBranchPicker(shortcutId), out var descriptor));
        Assert.Equal(CommandKind.WorktreeBranchPicker, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
    }

    [Fact]
    public void TryParse_worktree_branch_select()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(ShortcutCommandIds.WorktreeBranchSelect(shortcutId, "feature/x"), out var descriptor));
        Assert.Equal(CommandKind.WorktreeBranchSelect, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
        Assert.Equal("feature/x", descriptor.Branch);
    }

    [Fact]
    public void TryParse_worktree_branch_clear()
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(ShortcutCommandIds.WorktreeBranchClear(shortcutId), out var descriptor));
        Assert.Equal(CommandKind.WorktreeBranchClear, descriptor.Kind);
        Assert.Equal(shortcutId, descriptor.WorkspaceId);
    }

    [Theory]
    [InlineData(".admin")]
    [InlineData(".standard")]
    public void TryParse_open_launch_strips_variant_suffixes(string suffix)
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        var launchId = Guid.NewGuid().ToString("N");

        Assert.True(_parser.TryParse(ShortcutCommandIds.OpenLaunch(shortcutId, launchId) + suffix, out var launch));
        Assert.Equal(shortcutId, launch.WorkspaceId);
        Assert.Equal(launchId, launch.LaunchId);
    }

    [Theory]
    [InlineData(".admin")]
    [InlineData(".standard")]
    public void TryParse_open_strips_variant_suffixes(string suffix)
    {
        var shortcutId = Guid.NewGuid().ToString("N");
        Assert.True(_parser.TryParse(ShortcutCommandIds.Open(shortcutId) + suffix, out var open));
        Assert.Equal(shortcutId, open.WorkspaceId);
    }

    [Fact]
    public void OpenLaunch_parser_does_not_match_workspace_only_id()
    {
        var openId = ShortcutCommandIds.Open(Guid.NewGuid().ToString("N"));
        Assert.False(CommandIdParser.TryParseOpenLaunch(openId, out _, out _));
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
