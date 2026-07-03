using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLaunchNormalizationTests
{
    [Fact]
    public void EnsureLaunchesFromLegacy_SynthesizesSingleEntryFromTopLevelFields()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "My App",
            Directory = @"C:\Projects\MyApp",
            Command = "npm start",
            Terminal = "wt",
            WtProfile = "PowerShell",
            RunAsAdmin = true,
        };

        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);

        Assert.Single(shortcut.Launches);
        Assert.Equal("My App", shortcut.Launches[0].Label);
        Assert.Equal("npm start", shortcut.Launches[0].Command);
        Assert.Equal("wt", shortcut.Launches[0].Terminal);
        Assert.Equal("PowerShell", shortcut.Launches[0].WtProfile);
        Assert.True(shortcut.Launches[0].RunAsAdmin);
        Assert.True(shortcut.Launches[0].IsEnabled);
    }

    [Fact]
    public void NormalizeShortcut_MirrorsFirstLaunchToLegacyFields()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Agents",
            Directory = @"C:\Projects\Agents",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Claude",
                    Command = "claude",
                    Terminal = "wt",
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        Assert.Equal("claude", shortcut.Command);
        Assert.Equal("wt", shortcut.Terminal);
    }

    [Fact]
    public void MirrorLegacyFieldsFromFirstLaunch_UsesFirstEnabledEntry()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Multi",
            Directory = @"C:\Projects\Multi",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Disabled",
                    Command = "old",
                    Terminal = "cmd",
                    IsEnabled = false,
                    Order = 0,
                },
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = "Active",
                    Command = "npm start",
                    Terminal = "wt",
                    IsEnabled = true,
                    Order = 1,
                },
            ],
        };

        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        Assert.Equal("npm start", shortcut.Command);
        Assert.Equal("wt", shortcut.Terminal);
    }

    [Fact]
    public void TryValidateLaunches_RejectsDuplicateLabels()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Dup",
            Directory = Environment.CurrentDirectory,
            Launches =
            [
                new WorkspaceEntry { Id = "a", Label = "Main", Terminal = "default", IsEnabled = true, Order = 0 },
                new WorkspaceEntry { Id = "b", Label = "Main", Terminal = "default", IsEnabled = true, Order = 1 },
            ],
        };

        Assert.False(ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out var error));
        Assert.Contains("Duplicate", error, StringComparison.OrdinalIgnoreCase);
    }
}
