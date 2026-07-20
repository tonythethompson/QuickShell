namespace QuickShell.Services;

/// <summary>
/// Ambient app-data root, defaulting to <c>%LOCALAPPDATA%</c>. Backs <see cref="AppDataPaths"/>
/// for DI-resolved services, and is also the fallback for host glue code
/// (e.g. <c>QuickShellJsonSettingsStore</c>, <c>TerminalSettingsDiscovery</c>) that is
/// constructed directly rather than through the container — such code sits behind public
/// parameterless entry points (the CmdPal plugin contract) with no constructor seam to inject
/// a path into. <see cref="AsyncLocal{T}"/> flows only through the execution context that set
/// it (including its own child <c>Task.Run</c> calls), so unlike a shared mutable static it
/// never leaks into unrelated concurrently-running tests.
/// </summary>
internal static class AppDataRoot
{
    private static readonly AsyncLocal<string?> Override = new();

    public static string Current =>
        Override.Value ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    internal sealed class TestScope : IDisposable
    {
        private readonly string? _previous;

        public TestScope(string root)
        {
            _previous = Override.Value;
            Override.Value = root;
        }

        public void Dispose() => Override.Value = _previous;
    }
}
