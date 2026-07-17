using System.Diagnostics;
using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class ProcessStarter : IProcessStarter
{
    public bool TryStart(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        return Process.Start(startInfo) is not null;
    }
}
