namespace QuickShell.Services;

internal static class TerminalProfileIconResolver
{
    // Highest scale first: the host (and our own list-icon cache) downsamples for display,
    // so a larger source always looks at least as good. Picking .scale-100 first handed out
    // 16x16 sources that got upscaled instead of downsampled, which is why bundled icons
    // (PowerShell, Windows PowerShell, cmd) rendered blurry in the shortcut list.
    private static readonly string[] ScaleSuffixes =
    [
        ".scale-200",
        ".scale-150",
        ".scale-125",
        ".scale-100",
        "",
    ];

    public static string? ResolveEffectiveIcon(WtProfileInfo profile)
    {
        foreach (var candidate in GetIconCandidates(profile))
        {
            var resolved = Resolve(candidate, profile.SettingsPath);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    public static string? Resolve(
        string? icon,
        string settingsPath,
        IReadOnlyList<string>? installPaths = null)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var value = icon.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
        {
            return ResolvePackagedPath(value, "ms-appx:///", installPaths);
        }

        if (value.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveAppDataPath(value, settingsPath);
        }

        if (LooksLikeEmojiOrInlineGlyph(value))
        {
            return value;
        }

        if (Path.IsPathRooted(value))
        {
            return File.Exists(value) ? value : null;
        }

        var settingsDirectory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            return null;
        }

        var relativePath = Path.Combine(settingsDirectory, value);
        return File.Exists(relativePath) ? relativePath : null;
    }

    /// <summary>
    /// True when the profile icon is an emoji or Segoe glyph stored inline in WT settings,
    /// as opposed to a PNG path resolved from packaged or on-disk profile icon assets.
    /// </summary>
    public static bool IsInlineGlyphIcon(string? value) =>
        IsCmdPalGlyphIcon(value);

    public static bool IsCmdPalGlyphIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(value)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        return LooksLikeEmojiOrInlineGlyph(value);
    }

    private static IEnumerable<string> GetIconCandidates(WtProfileInfo profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Icon))
        {
            yield return profile.Icon;
        }

        foreach (var inferred in InferIconCandidates(profile))
        {
            yield return inferred;
        }
    }

    private static IEnumerable<string> InferIconCandidates(WtProfileInfo profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Guid))
        {
            var guid = profile.Guid.Trim('{', '}');
            yield return $"ms-appx:///ProfileIcons/{guid}.png";
            yield return $"ms-appx:///ProfileIcons/{{{guid}}}.png";
        }

        var name = profile.Name ?? string.Empty;
        var source = profile.ProfileSource ?? string.Empty;
        var commandline = profile.Commandline ?? string.Empty;

        if (source.Contains("VisualStudio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Developer", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("PowerShell", StringComparison.OrdinalIgnoreCase))
            {
                yield return "ms-appx:///ProfileIcons/vs-pwsh.png";
                yield return "ms-appx:///ProfileIcons/vs-powershell.png";
            }

            if (name.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase))
            {
                yield return "ms-appx:///ProfileIcons/vs-cmd.png";
            }
        }

        if (source.Contains("PowershellCore", StringComparison.OrdinalIgnoreCase)
            || commandline.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ms-appx:///ProfileIcons/pwsh.png";
        }

        if (commandline.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ms-appx:///ProfileIcons/{61c54bbd-c2c6-5271-96e7-009a87ff44bf}.png";
        }

        if (commandline.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ms-appx:///ProfileIcons/{0caa0dad-35be-5f56-a8ff-afceeeaa6101}.png";
        }
    }

    private static string? ResolvePackagedPath(
        string uri,
        string prefix,
        IReadOnlyList<string>? installPaths)
    {
        var relative = uri[prefix.Length..]
            .TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        foreach (var candidate in ExpandPackagedIconCandidates(relative))
        {
            foreach (var installPath in installPaths ?? WindowsTerminalInstallDiscovery.GetInstallPaths())
            {
                var fullPath = Path.Combine(installPath, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExpandPackagedIconCandidates(string relativePath)
    {
        yield return relativePath;

        var directory = Path.GetDirectoryName(relativePath);
        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            yield break;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetExtension(fileName).Length > 0
            ? fileName[..^extension.Length]
            : fileName;

        foreach (var suffix in ScaleSuffixes)
        {
            var scaledName = string.IsNullOrEmpty(extension)
                ? baseName + suffix
                : baseName + suffix + extension;
            yield return string.IsNullOrWhiteSpace(directory)
                ? scaledName
                : Path.Combine(directory, scaledName);
        }
    }

    private static string? ResolveAppDataPath(string uri, string settingsPath)
    {
        var packageDirectory = ResolvePackageDirectory(settingsPath);
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            return null;
        }

        const string localPrefix = "ms-appdata:///local/";
        const string roamingPrefix = "ms-appdata:///roaming/";

        string? relative = null;
        string? stateDirectory = null;

        if (uri.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = uri[localPrefix.Length..];
            stateDirectory = Path.Combine(packageDirectory, "LocalState");
        }
        else if (uri.StartsWith(roamingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = uri[roamingPrefix.Length..];
            stateDirectory = Path.Combine(packageDirectory, "RoamingState");
        }

        if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(stateDirectory))
        {
            return null;
        }

        relative = relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.Combine(stateDirectory, relative);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolvePackageDirectory(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        if (directory.EndsWith("LocalState", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(directory)?.FullName;
        }

        if (directory.EndsWith("Windows Terminal", StringComparison.OrdinalIgnoreCase))
        {
            return directory;
        }

        return null;
    }

    private static bool LooksLikeEmojiOrInlineGlyph(string value)
    {
        if (value.StartsWith("ms-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Contains('.', StringComparison.Ordinal) && value.Length > 4)
        {
            return false;
        }

        return true;
    }
}
