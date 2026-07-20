using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface IWtProfilesService
{
    void InvalidateCache();

    IReadOnlyList<WtProfileInfo> GetProfiles();

    IReadOnlyList<string> GetProfileNames();

    WtProfileInfo? FindProfileForLaunch(string? terminal, string? profileName);

    WtProfileInfo? FindProfileByNameAcrossHosts(string profileName);

    WtProfileInfo? FindDefaultProfile(string hostTerminal);

    WtProfileInfo? FindProfileForStandaloneShell(string shellId);

    IReadOnlyList<WtProfileInfo> GetProfilesForApplication(string terminalApplicationId);
}
