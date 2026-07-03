using QuickShell.Models;

namespace QuickShell.Services;

internal static class GitRepoSearchRoots
{
    public static IEnumerable<string> FromShortcuts(IReadOnlyList<TerminalShortcut> shortcuts) =>
        shortcuts
            .Select(shortcut => shortcut.Directory)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(TryGetParentDirectory)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>();

    private static string? TryGetParentDirectory(string directory)
    {
        try
        {
            return Path.GetDirectoryName(directory.Trim().TrimEnd('\\', '/'));
        }
        catch
        {
            return null;
        }
    }
}
