using QuickShell.Services;
using System.Text;

namespace QuickShell.Core.Tests;

public sealed class WtProfilesServiceCacheTests : IDisposable
{
    private readonly string _directory;

    public WtProfilesServiceCacheTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "quickshell-wt-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void RefreshCacheIfNeeded_ReparsesOnlyChangedSettingsFile()
    {
        var hostAPath = Path.Combine(_directory, "host-a.json");
        var hostBPath = Path.Combine(_directory, "host-b.json");
        WriteSettings(hostAPath, "Host A Profile");
        WriteSettings(hostBPath, "Host B Profile");

        var locations =
            new[]
            {
                CreateLocation(hostAPath, "wt-a", "Host A"),
                CreateLocation(hostBPath, "wt-b", "Host B"),
            };

        var parseCount = 0;
        var service = new WtProfilesService(locations, () => parseCount++);

        var first = service.GetProfiles();
        Assert.Equal(2, first.Count);
        Assert.Equal(2, parseCount);

        var profiles = service.GetProfiles();
        Assert.Equal(2, profiles.Count);
        Assert.Equal(2, parseCount);

        WriteSettings(hostAPath, "Host A Updated");
        File.SetLastWriteTimeUtc(hostAPath, DateTime.UtcNow.AddSeconds(5));

        profiles = service.GetProfiles();

        Assert.Equal(2, profiles.Count);
        Assert.Equal(3, parseCount);
        Assert.Contains(profiles, profile => profile.Name.Equals("Host A Updated", StringComparison.Ordinal));
        Assert.Contains(profiles, profile => profile.Name.Equals("Host B Profile", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidateCache_ClearsPerFileCache()
    {
        var hostPath = Path.Combine(_directory, "host.json");
        WriteSettings(hostPath, "Only Profile");
        var location = CreateLocation(hostPath, "wt", "Host");

        var parseCount = 0;
        var service = new WtProfilesService([location], () => parseCount++);

        _ = service.GetProfiles();
        Assert.Equal(1, parseCount);

        service.InvalidateCache();

        _ = service.GetProfiles();
        Assert.Equal(2, parseCount);
    }

    private static TerminalSettingsLocation CreateLocation(string settingsPath, string idPrefix, string label) =>
        new()
        {
            SettingsPath = settingsPath,
            Source = TerminalSettingsSource.WindowsTerminal,
            HostExecutable = "wt.exe",
            IdPrefix = idPrefix,
            DisplayPrefix = label,
        };

    private static void WriteSettings(string path, string profileName)
    {
        var json =
            $$"""
              {
                "profiles": {
                  "list": [
                    {
                      "name": "{{profileName}}",
                      "guid": "{11111111-1111-1111-1111-111111111111}"
                    }
                  ]
                }
              }
              """;
        File.WriteAllText(path, json, Encoding.UTF8);
    }
}
