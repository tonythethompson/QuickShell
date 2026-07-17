using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface ITerminalLauncher
{
    ResolvedLaunch Resolve(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId);

    TerminalLaunchAttempt Open(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool runAsAdmin = false,
        bool runAsStandard = false);

    TerminalLaunchAttempt OpenResolved(ResolvedLaunch resolved, bool effectiveElevation);

    IReadOnlyList<TerminalLaunchAttempt> OpenGroup(
        IReadOnlyList<ResolvedLaunch> group,
        bool effectiveElevation,
        string? hostExecutableOverride = null);
}
