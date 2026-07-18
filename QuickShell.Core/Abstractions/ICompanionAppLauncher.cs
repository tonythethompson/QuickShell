using QuickShell.Models;

namespace QuickShell.Abstractions;

using QuickShell.Services;

internal interface ICompanionAppLauncher
{
    bool IsConfigured(TerminalShortcut shortcut);

    bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut);

    CompanionLaunchResult Launch(TerminalShortcut shortcut, bool onDemand);

    bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error);

    string BuildDisplaySummary(TerminalShortcut shortcut);
}
