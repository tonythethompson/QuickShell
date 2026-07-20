using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface ITerminalCatalog
{
    const string SameAsPreviousLaunchTargetId = "same-as-previous";

    const string SameAsPreviousDisplayName = "Same as previous command";

    IReadOnlyList<LaunchTarget> GetLaunchTargets(bool includeDefaultChoice = false);

    void InvalidateCache();

    string GetFingerprint();

    IReadOnlyList<string> GetDefaultProfileIds(string terminalApplicationId);

    bool HasTerminalApplication(string terminalApplicationId);

    IReadOnlyList<WtProfileInfo> GetProfilesForApplication(string terminalApplicationId);

    string GetDisplayName(TerminalShortcut shortcut);

    string GetProfileLabel(TerminalShortcut shortcut);

    string EncodeLaunchTargetId(TerminalShortcut shortcut);

    void ApplyLaunchTargetId(TerminalShortcut shortcut, string? launchTargetId);

    LaunchTarget Resolve(string? launchTargetId);

    LaunchTarget ResolveForShortcut(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId);

    LaunchTarget ResolveForShortcut(TerminalShortcut shortcut, string defaultLaunchTargetId);

    bool IsStandaloneShellLaunchTarget(string? launchTargetId);

    string BuildFormChoicesJson(bool includeDefaultChoice, string terminalApplicationId);

    string BuildFormChoicesJson(bool includeDefaultChoice);

    string ResolveEffectiveLaunchTargetId(
        IReadOnlyList<WorkspaceEntry> orderedLaunches,
        int index);

    WorkspaceEntry ResolveLaunchEntry(
        WorkspaceEntry entry,
        IReadOnlyList<WorkspaceEntry> orderedLaunches,
        int index);

    string NormalizeLaunchTargetId(string? launchTargetId);
}
