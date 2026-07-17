using System.Diagnostics;

namespace QuickShell.Abstractions;

/// <summary>
/// Process-start boundary used by launchers. Production starts real processes;
/// tests inject a capturing fake so no process-wide static override is required.
/// </summary>
internal interface IProcessStarter
{
    /// <summary>
    /// Starts a process. Returns <c>false</c> if the process could not be started
    /// (equivalent to <see cref="Process.Start(ProcessStartInfo)"/> returning null).
    /// </summary>
    bool TryStart(ProcessStartInfo startInfo);
}
