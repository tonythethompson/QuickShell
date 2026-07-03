using QuickShell;
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
}
