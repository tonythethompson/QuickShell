using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection(PathExecutableLookupIsolation.Name)]
public sealed class TerminalCatalogDiscoveryTests : IDisposable
{
    private readonly Func<string, string?>? _previous;
    private readonly string _tempRoot;

    public TerminalCatalogDiscoveryTests()
    {
        _previous = PathExecutableLookup.TryResolveOverride;
        PathExecutableLookup.TryResolveOverride = null;
        _tempRoot = Path.Join(Path.GetTempPath(), "qs-term-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        PathExecutableLookup.TryResolveOverride = _previous;
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    [Fact]
    public void GetLaunchTargets_PathOverrideHit_IncludesPwsh()
    {
        PathExecutableLookup.TryResolveOverride = name =>
            name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.Join(_tempRoot, "pwsh.exe")
                : null;

        var catalog = new TerminalCatalog(new WtProfilesService(locations: []));
        catalog.InvalidateCache();
        var targets = catalog.GetLaunchTargets();

        Assert.Contains(targets, t => t.Id.Equals("pwsh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(targets, t => t.Id.Equals("cmd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetLaunchTargets_System32KnownLocations_IncludeCmdAndPowerShell()
    {
        PathExecutableLookup.TryResolveOverride = null;

        var catalog = new TerminalCatalog(new WtProfilesService(locations: []));
        catalog.InvalidateCache();
        var targets = catalog.GetLaunchTargets();

        Assert.Contains(targets, t => t.Id.Equals("cmd", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(targets, t => t.Id.Equals("powershell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetLaunchTargets_WindowsTerminal_FromSettingsWithoutPath()
    {
        var localAppData = Path.Join(_tempRoot, "localappdata");
        var settingsDir = Path.Join(localAppData, "Microsoft", "Windows Terminal");
        Directory.CreateDirectory(settingsDir);
        var settingsPath = Path.Join(settingsDir, "settings.json");
        File.WriteAllText(settingsPath, """{"profiles":{"list":[]}}""");

        PathExecutableLookup.TryResolveOverride = name =>
            name.Equals("wt.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wtai.exe", StringComparison.OrdinalIgnoreCase)
                ? null
                : PathExecutableLookup.TryResolveKnownLocation(name, out var known)
                    ? known
                    : null;

        using var scope = new AppDataRoot.TestScope(localAppData);
        var catalog = new TerminalCatalog(new WtProfilesService());
        catalog.InvalidateCache();

        Assert.True(catalog.HasTerminalApplication(TerminalHostIds.WindowsTerminal));
        Assert.Contains(
            catalog.GetLaunchTargets(),
            t => t.Id.Equals(TerminalHostIds.WindowsTerminal, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidateCache_RebuildsLaunchTargets_WithoutWhereExe()
    {
        PathExecutableLookup.TryResolveOverride = null;

        var catalog = new TerminalCatalog(new WtProfilesService(locations: []));
        var first = catalog.GetLaunchTargets();
        var firstFingerprint = catalog.GetFingerprint();

        catalog.InvalidateCache();
        var second = catalog.GetLaunchTargets();
        var secondFingerprint = catalog.GetFingerprint();

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(firstFingerprint, secondFingerprint);
        Assert.Contains(second, t => t.Id.Equals("cmd", StringComparison.OrdinalIgnoreCase));
    }
}
