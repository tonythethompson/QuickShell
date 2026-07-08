using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CommandIdParserTests
{
    private readonly CommandIdParser _parser = new();

    [Fact]
    public void TryParse_settings_ids_map_to_OpenSettings()
    {
        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.Settings, out var settings));
        Assert.Equal(CommandKind.OpenSettings, settings.Kind);

        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.ImportConflict, out var conflict));
        Assert.Equal(CommandKind.OpenSettings, conflict.Kind);

        Assert.True(_parser.TryParse(QuickShellDeepLinkIds.PendingShortcutEdit, out var pending));
        Assert.Equal(CommandKind.OpenSettings, pending.Kind);
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
        var directory = @"A:\repos\QuickShell";
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
        Assert.Null(open.LaunchId);
    }

    [Fact]
    public void TryParse_unknown_returns_false()
    {
        Assert.False(_parser.TryParse("com.other.extension.foo", out _));
        Assert.False(_parser.TryParse(string.Empty, out _));
    }
}
