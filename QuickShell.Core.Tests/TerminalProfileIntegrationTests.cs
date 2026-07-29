using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Services;
using System.Text;

namespace QuickShell.Core.Tests;

public sealed class TerminalProfileIntegrationTests
{
    [Fact]
    public void GetForLaunch_ReturnsGlyphForExplicitTerminalKind()
    {
                WindowsTerminalInstallDiscovery.InvalidateCache();

        var launch = new WorkspaceEntry { Terminal = "pwsh", IsEnabled = true };
        var icon = new TerminalLaunchGlyphs(new TerminalProfileResolver(new QuickShellSettingsReader(), new WtProfilesService(), new TerminalCatalog(new WtProfilesService()))).GetForLaunch(launch);

        Assert.False(string.IsNullOrWhiteSpace(icon));
    }

    [Fact]
    public void ReadProfilesFromJson_AppliesNestedDefaultProfile()
    {
        var location = new TerminalSettingsLocation
        {
            SettingsPath = @"C:\Settings\settings.json",
            Source = TerminalSettingsSource.WindowsTerminal,
            HostExecutable = "wt.exe",
            IdPrefix = "wt",
            DisplayPrefix = "Windows Terminal",
        };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "profiles": {
                "defaultProfile": "{22222222-2222-2222-2222-222222222222}",
                "list": [
                  {
                    "name": "PowerShell",
                    "guid": "{11111111-1111-1111-1111-111111111111}"
                  },
                  {
                    "name": "Ubuntu",
                    "guid": "{22222222-2222-2222-2222-222222222222}"
                  }
                ]
              }
            }
            """));

        var profiles = WtProfilesService.ReadProfilesFromJson(stream, location);

        Assert.Collection(
            profiles,
            profile => Assert.False(profile.IsDefault),
            profile => Assert.True(profile.IsDefault));
    }

    [Fact]
    public void ReadProfilesFromJson_MergesFragmentIconAndCommandline()
    {
        var location = new TerminalSettingsLocation
        {
            SettingsPath = @"C:\Settings\settings.json",
            Source = TerminalSettingsSource.WindowsTerminal,
            HostExecutable = "wt.exe",
            IdPrefix = "wt",
            DisplayPrefix = "Windows Terminal",
        };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "profiles": {
                "list": [
                  {
                    "name": "Nushell",
                    "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                    "source": "nu"
                  }
                ]
              }
            }
            """));

        var fragments = new Dictionary<string, TerminalFragmentProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["47302f9c-1ac4-566c-aa3e-8cf29889d6ab"] = new()
            {
                Commandline = @"C:\Users\tony\nu\bin\nu.exe",
                Icon = @"C:\Users\tony\nu\nu.ico",
            },
        };

        var profiles = WtProfilesService.ReadProfilesFromJson(stream, location, fragments);

        var profile = Assert.Single(profiles);
        Assert.Equal(@"C:\Users\tony\nu\bin\nu.exe", profile.Commandline);
        Assert.Equal(@"C:\Users\tony\nu\nu.ico", profile.Icon);
    }
}
