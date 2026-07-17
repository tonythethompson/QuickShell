namespace QuickShell.Abstractions;

/// <summary>
/// Host/environment probes used by workspace health checks.
/// Production uses real filesystem/process/socket inspection; tests inject fakes.
/// </summary>
internal interface IWorkspaceEnvironmentProbe
{
    bool ExecutableExists(string path);

    bool PortInUse(int port);

    IReadOnlyList<string> ProcessNames();

    IReadOnlyList<string> WslDistroNames();
}
