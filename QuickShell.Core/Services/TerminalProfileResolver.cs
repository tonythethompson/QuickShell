using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class TerminalProfileResolver : ITerminalProfileResolver
{
    private readonly QuickShellSettingsReader _settingsReader;
    private readonly IWtProfilesService _profiles;
    private readonly ITerminalCatalog _catalog;

    public TerminalProfileResolver(
        QuickShellSettingsReader settingsReader,
        IWtProfilesService profiles,
        ITerminalCatalog catalog)
    {
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public WtProfileInfo? ResolveForLaunch(WorkspaceEntry launch)
    {
        var terminal = (launch.Terminal ?? "default").Trim().ToLowerInvariant();

        if (terminal is "wt" or "it" or "windows-terminal" or "intelligent-terminal")
        {
            var hostTerminal = NormalizeHostTerminal(terminal);
            if (!string.IsNullOrWhiteSpace(launch.WtProfile))
            {
                return _profiles.FindProfileForLaunch(hostTerminal, launch.WtProfile);
            }

            return _profiles.FindDefaultProfile(hostTerminal);
        }

        if (terminal == "default")
        {
            return ResolveDefaultSettingsProfile();
        }

        if (terminal is "pwsh" or "powershell" or "powershell7" or "cmd")
        {
            return _profiles.FindProfileForStandaloneShell(terminal);
        }

        if (terminal == "wsl" && !string.IsNullOrWhiteSpace(launch.WtProfile))
        {
            return _profiles.FindProfileByNameAcrossHosts(launch.WtProfile);
        }

        return null;
    }

    private WtProfileInfo? ResolveDefaultSettingsProfile()
    {
        var terminalApplicationId = _settingsReader.TerminalApplicationId;
        if (terminalApplicationId.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hostTerminal = terminalApplicationId.Equals(
            TerminalHostIds.IntelligentTerminal,
            StringComparison.OrdinalIgnoreCase)
            ? TerminalHostIds.IntelligentTerminal
            : TerminalHostIds.WindowsTerminal;

        var defaultProfileId = _settingsReader.DefaultProfileId;
        if (defaultProfileId.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase))
        {
            return _profiles.FindDefaultProfile(hostTerminal);
        }

        if (_catalog.IsStandaloneShellLaunchTarget(defaultProfileId))
        {
            return _profiles.FindProfileForStandaloneShell(defaultProfileId)
                ?? _profiles.FindDefaultProfile(hostTerminal);
        }

        return _profiles.FindProfileForLaunch(hostTerminal, defaultProfileId)
            ?? _profiles.FindProfileByNameAcrossHosts(defaultProfileId);
    }

    private static string NormalizeHostTerminal(string terminal) =>
        terminal is "it" or "intelligent-terminal"
            ? TerminalHostIds.IntelligentTerminal
            : TerminalHostIds.WindowsTerminal;
}
