using System.Text;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Stubs terminal discovery and health preflight so launch executor tests do not
/// depend on what is installed on the machine running CI.
/// </summary>
internal static class LaunchExecutorTestEnvironment
{
    private static string? _settingsDirectory;

    public static void Apply()
    {
        Reset();

        _settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "qs-launch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDirectory);

        var settingsPath = Path.Combine(_settingsDirectory, "settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "profiles": {
                "list": [
                  {
                    "name": "Test",
                    "guid": "{11111111-1111-1111-1111-111111111111}"
                  }
                ]
              }
            }
            """,
            Encoding.UTF8);

        WtProfilesService.TestLocationsOverride =
        [
            new TerminalSettingsLocation
            {
                SettingsPath = settingsPath,
                Source = TerminalSettingsSource.WindowsTerminal,
                HostExecutable = "wt.exe",
                IdPrefix = "wt",
                DisplayPrefix = "Windows Terminal",
            },
        ];
        WtProfilesService.InvalidateCache();
        TerminalCatalog.InvalidateCache();

        WorkspaceHealthCheck.ExecutableExistsOverride = _ => true;
        WorkspaceHealthCheck.PortInUseOverride = _ => false;
        WorkspaceHealthCheck.ProcessNamesOverride = () => [];
        WorkspaceHealthCheck.WslDistroNamesOverride = () => ["Ubuntu"];
    }

    public static void Reset()
    {
        WtProfilesService.TestLocationsOverride = null;
        WtProfilesService.InvalidateCache();
        TerminalCatalog.InvalidateCache();

        WorkspaceHealthCheck.ExecutableExistsOverride = null;
        WorkspaceHealthCheck.PortInUseOverride = null;
        WorkspaceHealthCheck.ProcessNamesOverride = null;
        WorkspaceHealthCheck.WslDistroNamesOverride = null;

        if (_settingsDirectory is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }

        _settingsDirectory = null;
    }
}
