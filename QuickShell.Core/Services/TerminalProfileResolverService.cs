using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class TerminalProfileResolverService : ITerminalProfileResolver
{
    public WtProfileInfo? ResolveForLaunch(WorkspaceEntry launch) =>
        TerminalProfileResolver.ResolveForLaunch(launch);
}
