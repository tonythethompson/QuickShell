using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class TerminalProfileIntegrationTests
{
    [Fact]
    public void GetForLaunch_ReturnsGlyphForExplicitTerminalKind()
    {
        WtProfilesService.InvalidateCache();
        WindowsTerminalInstallDiscovery.InvalidateCache();

        var launch = new WorkspaceEntry { Terminal = "pwsh", IsEnabled = true };
        var icon = TerminalLaunchGlyphs.GetForLaunch(launch);

        Assert.False(string.IsNullOrWhiteSpace(icon));
    }
}
