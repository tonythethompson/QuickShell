namespace QuickShell.Services;

/// <summary>
/// Lexical workspace path rules. No filesystem access except <see cref="DirectoryExists"/>.
/// WSL paths use case-sensitive comparison; Windows drive and ordinary UNC paths are case-insensitive.
/// </summary>
internal static class WorkspacePath
{
    public static bool TryNormalizeLexical(string? path, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Directory is required.";
            return false;
        }

        var trimmed = path.Trim();

        if (TryNormalizeExtendedWindowsPath(trimmed, out normalized, out error))
        {
            return true;
        }

        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            error = "Linux-style paths must use a \\\\wsl$\\distro\\... UNC path for workspace storage.";
            return false;
        }

        if (WslPathResolver.TryParse(trimmed, out var wslLocation))
        {
            normalized = NormalizeWslLexical(trimmed, wslLocation);
            return !string.IsNullOrWhiteSpace(normalized);
        }

        if (!Path.IsPathRooted(trimmed))
        {
            error = "Directory must be an absolute path.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Directory path is not valid.";
            return false;
        }

        normalized = TrimTrailingSeparatorsPreserveRoot(normalized);
        return true;
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (!TryNormalizeLexical(left, out var normalizedLeft, out _))
        {
            return false;
        }

        if (!TryNormalizeLexical(right, out var normalizedRight, out _))
        {
            return false;
        }

        if (IsWslUncPath(normalizedLeft) || IsWslUncPath(normalizedRight))
        {
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    public static bool DirectoryExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ShortcutValidation.DirectoryExists(path);
    }

    public static bool HasConfiguredLexicalDirectory(string? path) =>
        TryNormalizeLexical(path, out _, out _);

    public static bool IsEmptyOrWhitespace(string? path) => string.IsNullOrWhiteSpace(path);

    public static string TrimTrailingSeparatorsForStorage(string path) =>
        TrimTrailingSeparatorsPreserveRoot(path);

    private static bool TryNormalizeExtendedWindowsPath(string trimmed, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (!trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return false;
        }

        var withoutPrefix = trimmed[4..];
        if (withoutPrefix.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
        {
            withoutPrefix = @"\\" + withoutPrefix[4..];
        }

        if (WslPathResolver.TryParse(withoutPrefix, out var wslLocation))
        {
            normalized = NormalizeWslLexical(withoutPrefix, wslLocation);
            return !string.IsNullOrWhiteSpace(normalized);
        }

        if (!Path.IsPathRooted(withoutPrefix))
        {
            error = "Directory must be an absolute path.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(withoutPrefix);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Directory path is not valid.";
            return false;
        }

        normalized = TrimTrailingSeparatorsPreserveRoot(normalized);
        return true;
    }

    private static string NormalizeWslLexical(string original, WslPathResolver.WslLocation location)
    {
        if (!string.IsNullOrWhiteSpace(location.UncPath))
        {
            return TrimTrailingSeparatorsPreserveRoot(location.UncPath.Replace('/', '\\'));
        }

        return TrimTrailingSeparatorsPreserveRoot(original.Replace('/', '\\'));
    }

    private static bool IsWslUncPath(string path) =>
        path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSeparatorsPreserveRoot(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (path.Length == 2 && path[1] == ':')
        {
            return path + '\\';
        }

        if (path.Length == 3 && path[1] == ':' && path[2] == '\\')
        {
            return path;
        }

        return path.TrimEnd('\\', '/');
    }
}
