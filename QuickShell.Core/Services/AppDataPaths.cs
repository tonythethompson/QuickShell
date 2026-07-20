using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class AppDataPaths : IAppDataPaths
{
    private readonly string? _explicitRoot;

    public AppDataPaths(string? root = null)
    {
        _explicitRoot = root;
    }

    public string Root => _explicitRoot ?? AppDataRoot.Current;
}
