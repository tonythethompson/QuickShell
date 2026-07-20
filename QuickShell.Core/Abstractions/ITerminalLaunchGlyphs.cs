using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface ITerminalLaunchGlyphs
{
    string GetForShortcut(TerminalShortcut shortcut);

    string GetForList(TerminalShortcut shortcut);

    string GetForList(WorkspaceEntry launch);

    string GetForLaunch(WorkspaceEntry launch);
}
