using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class TerminalLauncherService : ITerminalLauncher
{
    public ResolvedLaunch Resolve(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId) =>
        TerminalLauncher.Resolve(shortcut, terminalApplicationId, defaultProfileId);

    public TerminalLaunchAttempt Open(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        bool runAsAdmin = false,
        bool runAsStandard = false) =>
        TerminalLauncher.Open(shortcut, terminalApplicationId, defaultProfileId, runAsAdmin, runAsStandard);

    public TerminalLaunchAttempt OpenResolved(ResolvedLaunch resolved, bool effectiveElevation) =>
        TerminalLauncher.OpenResolved(resolved, effectiveElevation);
}
