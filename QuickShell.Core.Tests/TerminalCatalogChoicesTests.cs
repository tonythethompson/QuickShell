using QuickShell.Abstractions;
using QuickShell;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class TerminalCatalogChoicesTests
{
    [Fact]
    public void GetMinimalDefaultProfileChoices_ContainsOnlyDefaultProfile()
    {
        var choices = TerminalCatalogChoices.GetMinimalDefaultProfileChoices();

        var choice = Assert.Single(choices);
        Assert.Equal(TerminalHostIds.DefaultProfile, choice.Value);
    }

    [Fact]
    public void GetProfileLabel_ReturnsProfileNameWithoutHostPrefix()
    {
        var shortcut = new TerminalShortcut
        {
            Terminal = "it",
            WtProfile = "Nushell",
        };

        Assert.Equal("Nushell", new TerminalCatalog(new WtProfilesService()).GetProfileLabel(shortcut));
    }

    [Fact]
    public void ResolveEffectiveLaunchTargetId_FollowsSameAsPreviousChain()
    {
        var launches = new List<WorkspaceEntry>
        {
            new() { Terminal = "default" },
            new() { Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId },
            new() { Terminal = "it", WtProfile = "Nushell" },
            new() { Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId },
        };

        Assert.Equal("default", TerminalCatalog.ResolveEffectiveLaunchTargetId(launches, 0));
        Assert.Equal("default", TerminalCatalog.ResolveEffectiveLaunchTargetId(launches, 1));
        Assert.Equal("default", TerminalCatalog.ResolveEffectiveLaunchTargetId(launches, 2));
        Assert.Equal("it:Nushell", TerminalCatalog.ResolveEffectiveLaunchTargetId(launches, 3));
        Assert.Equal("it:Nushell", TerminalCatalog.ResolveEffectiveLaunchTargetId(launches, 4));
    }

    [Fact]
    public void NormalizeShortcut_PreservesSameAsPreviousTerminal()
    {
        var shortcut = new TerminalShortcut
        {
            Launches =
            [
                new WorkspaceEntry
                {
                    Label = "Test",
                    Command = "npm test",
                    Terminal = TerminalCatalog.SameAsPreviousLaunchTargetId,
                    IsEnabled = true,
                    Order = 0,
                },
            ],
        };

        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        Assert.Equal(TerminalCatalog.SameAsPreviousLaunchTargetId, shortcut.Launches[0].Terminal);
    }

    [Fact]
    public void GetProfileLabel_StandalonePwsh_ReturnsPowerShell7()
    {
        var shortcut = new TerminalShortcut { Terminal = "pwsh" };

        Assert.Equal("PowerShell 7", new TerminalCatalog(new WtProfilesService()).GetProfileLabel(shortcut));
    }
}
