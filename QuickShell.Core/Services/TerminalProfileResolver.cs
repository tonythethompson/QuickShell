using QuickShell.Models;

namespace QuickShell.Services;

internal static class TerminalProfileResolver
{
    public static WtProfileInfo? ResolveForLaunch(WorkspaceEntry launch)
    {
        var terminal = (launch.Terminal ?? "default").Trim().ToLowerInvariant();

        if (terminal is "wt" or "it" or "windows-terminal" or "intelligent-terminal")
        {
            var hostTerminal = NormalizeHostTerminal(terminal);
            if (!string.IsNullOrWhiteSpace(launch.WtProfile))
            {
                return WtProfilesService.FindProfileForLaunch(hostTerminal, launch.WtProfile);
            }

            return WtProfilesService.FindDefaultProfile(hostTerminal);
        }

        if (terminal == "default")
        {
            return ResolveDefaultSettingsProfile();
        }

        if (terminal is "pwsh" or "powershell" or "powershell7" or "cmd")
        {
            return WtProfilesService.FindProfileForStandaloneShell(terminal);
        }

        if (terminal == "wsl" && !string.IsNullOrWhiteSpace(launch.WtProfile))
        {
            return WtProfilesService.FindProfileByNameAcrossHosts(launch.WtProfile);
        }

        return null;
    }

    private static WtProfileInfo? ResolveDefaultSettingsProfile()
    {
        var settings = new QuickShellSettingsReader();
        var terminalApplicationId = settings.TerminalApplicationId;
        if (terminalApplicationId.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hostTerminal = terminalApplicationId.Equals(
            TerminalHostIds.IntelligentTerminal,
            StringComparison.OrdinalIgnoreCase)
            ? TerminalHostIds.IntelligentTerminal
            : TerminalHostIds.WindowsTerminal;

        var defaultProfileId = settings.DefaultProfileId;
        if (defaultProfileId.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase))
        {
            return WtProfilesService.FindDefaultProfile(hostTerminal);
        }

        if (TerminalCatalog.IsStandaloneShellLaunchTarget(defaultProfileId))
        {
            return WtProfilesService.FindProfileForStandaloneShell(defaultProfileId)
                ?? WtProfilesService.FindDefaultProfile(hostTerminal);
        }

        return WtProfilesService.FindProfileForLaunch(hostTerminal, defaultProfileId)
            ?? WtProfilesService.FindProfileByNameAcrossHosts(defaultProfileId);
    }

    private static string NormalizeHostTerminal(string terminal) =>
        terminal is "it" or "intelligent-terminal"
            ? TerminalHostIds.IntelligentTerminal
            : TerminalHostIds.WindowsTerminal;
}
