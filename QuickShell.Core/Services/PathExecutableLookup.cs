namespace QuickShell.Services;

/// <summary>
/// In-process PATH / known-location executable resolution. Prefer this over spawning
/// <c>where.exe</c> on hot startup paths.
/// </summary>
internal static class PathExecutableLookup
{
    /// <summary>
    /// Test seam. When set, replaces PATH and known-location probing for
    /// <see cref="TryResolve"/> / <see cref="Exists"/>.
    /// </summary>
    internal static Func<string, string?>? TryResolveOverride { get; set; }

    public static bool Exists(string fileName) => TryResolve(fileName, out _);

    public static bool TryResolve(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var trimmed = fileName.Trim();
        if (TryResolveOverride is { } overrideResolve)
        {
            var resolved = overrideResolve(trimmed);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return false;
            }

            fullPath = resolved;
            return true;
        }

        if (TryResolveKnownLocation(trimmed, out fullPath))
        {
            return true;
        }

        return TryFindOnPath(trimmed, out fullPath);
    }

    /// <summary>
    /// Resolves stock Windows shells that live under System32 even when PATH is incomplete.
    /// </summary>
    public static bool TryResolveKnownLocation(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileName(fileName.Trim());
        try
        {
            var systemDirectory = Environment.SystemDirectory;
            if (string.IsNullOrWhiteSpace(systemDirectory))
            {
                return false;
            }

            foreach (var candidate in EnumerateKnownLocationCandidates(systemDirectory, name))
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                fullPath = Path.GetFullPath(candidate);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
            or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateKnownLocationCandidates(string systemDirectory, string fileName)
    {
        if (fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(systemDirectory, fileName);
            yield break;
        }

        if (fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Stock Windows installs keep Windows PowerShell under System32\WindowsPowerShell\v1.0,
            // not as a direct System32 sibling of cmd.exe.
            yield return Path.Join(systemDirectory, "WindowsPowerShell", "v1.0", fileName);
            yield return Path.Join(systemDirectory, fileName);
        }
    }

    public static bool TryFindOnPath(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        var name = Path.GetFileName(fileName.Trim());
        foreach (var segment in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Join(segment, name);
                if (!File.Exists(candidate))
                {
                    continue;
                }

                fullPath = Path.GetFullPath(candidate);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Skip invalid PATH segments.
            }
        }

        return false;
    }
}
