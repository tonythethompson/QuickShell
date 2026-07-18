using QuickShell.Models;

namespace QuickShell.Abstractions;

internal interface ICompanionAppLauncher
{
    bool IsConfigured(TerminalShortcut shortcut);

    bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut);

    IReadOnlyList<string> LastStartedExecutables { get; }

    bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error);

    string BuildDisplaySummary(TerminalShortcut shortcut);
}
