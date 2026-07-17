using QuickShell.Models;

namespace QuickShell.Abstractions;

internal interface ICompanionAppLauncher
{
    bool IsConfigured(TerminalShortcut shortcut);

    bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut);

    bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error);

    string BuildDisplaySummary(TerminalShortcut shortcut);
}
