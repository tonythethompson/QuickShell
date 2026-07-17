using QuickShell.Abstractions;

namespace QuickShell.Core.Tests;

/// <summary>
/// Configurable environment probe for health-check tests.
/// </summary>
internal sealed class FakeWorkspaceEnvironmentProbe : IWorkspaceEnvironmentProbe
{
    public Func<string, bool> ExecutableExistsHandler { get; set; } = _ => true;

    public Func<int, bool> PortInUseHandler { get; set; } = _ => false;

    public Func<IReadOnlyList<string>> ProcessNamesHandler { get; set; } = () => [];

    public Func<IReadOnlyList<string>> WslDistroNamesHandler { get; set; } = () => ["Ubuntu"];

    public bool ExecutableExists(string path) => ExecutableExistsHandler(path);

    public bool PortInUse(int port) => PortInUseHandler(port);

    public IReadOnlyList<string> ProcessNames() => ProcessNamesHandler();

    public IReadOnlyList<string> WslDistroNames() => WslDistroNamesHandler();

    public static FakeWorkspaceEnvironmentProbe Healthy() => new();
}
