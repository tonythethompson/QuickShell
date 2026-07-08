using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Abstractions;

internal interface ITerminalProfileResolver
{
    WtProfileInfo? ResolveForLaunch(WorkspaceEntry launch);
}
