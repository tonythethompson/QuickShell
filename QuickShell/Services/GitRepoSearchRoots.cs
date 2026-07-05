using QuickShell.Models;

namespace QuickShell.Services;

internal static class GitRepoSearchRoots
{
    public static IEnumerable<string> FromShortcuts(IReadOnlyList<TerminalShortcut> shortcuts) =>
        shortcuts
            .Select(shortcut => shortcut.Directory)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .SelectMany(GetSearchRootsForDirectory)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>();

    private static IEnumerable<string?> GetSearchRootsForDirectory(string directory)
    {
        yield return TryNormalizeDirectory(directory);
        yield return TryGetParentDirectory(directory);
    }

    private static string? TryNormalizeDirectory(string directory)
    {
        try
        {
            return WorkspacePath.TryNormalizeLexical(directory, out var normalized, out _)
                ? normalized
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetParentDirectory(string directory)
    {
        try
        {
            var parent = Path.GetDirectoryName(directory.Trim().TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(parent))
            {
                return null;
            }

            var driveRoot = Path.GetPathRoot(parent);
            if (!string.IsNullOrWhiteSpace(driveRoot)
                && string.Equals(parent, driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return parent;
        }
        catch
        {
            return null;
        }
    }
}
