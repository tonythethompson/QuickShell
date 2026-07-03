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

        Assert.Equal("Nushell", TerminalCatalog.GetProfileLabel(shortcut));
    }

    [Fact]
    public void GetProfileLabel_StandalonePwsh_ReturnsPowerShell7()
    {
        var shortcut = new TerminalShortcut { Terminal = "pwsh" };

        Assert.Equal("PowerShell 7", TerminalCatalog.GetProfileLabel(shortcut));
    }
}
