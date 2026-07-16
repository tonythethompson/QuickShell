using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutValidation
{
    public const int MaxNameLength = 120;
    public const int MaxAbbreviationLength = 32;
    public const int MaxDirectoryLength = 1024;
    public const int MaxCommandLength = 4000;
    public const int MaxWtProfileLength = 120;
    public const int MaxLinkUrlLength = 2048;
    public const int MaxCompanionAppPathLength = 1024;
    public const int MaxCompanionAppArgumentsLength = 2048;
    public const int MaxShortcutCount = 500;

    public static bool TryValidate(TerminalShortcut shortcut, out string error) =>
        TryValidate(shortcut, requireDirectoryExists: true, out error);

    public static bool TryValidate(TerminalShortcut shortcut, bool requireDirectoryExists, out string error)
    {
        if (string.IsNullOrWhiteSpace(shortcut.Name))
        {
            error = "Name is required.";
            return false;
        }

        if (shortcut.Name.Length > MaxNameLength)
        {
            error = $"Name must be {MaxNameLength} characters or fewer.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Abbreviation) && shortcut.Abbreviation.Length > MaxAbbreviationLength)
        {
            error = $"Home keyword must be {MaxAbbreviationLength} characters or fewer.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(shortcut.Directory))
        {
            error = "Directory is required.";
            return false;
        }

        if (shortcut.Directory.Length > MaxDirectoryLength)
        {
            error = $"Directory must be {MaxDirectoryLength} characters or fewer.";
            return false;
        }

        if (!TryNormalizeDirectory(shortcut.Directory, out var normalizedDirectory, out error))
        {
            return false;
        }

        shortcut.Directory = normalizedDirectory;

        if (!TryValidateCommand(shortcut.Command, out error))
        {
            return false;
        }

        if (!TryValidateWtProfile(shortcut.WtProfile, out error))
        {
            return false;
        }

        if (!ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out error))
        {
            return false;
        }

        if (!TryValidateOptionalLinkUrl(shortcut.DevServerUrl, out error, out var normalizedDevServer))
        {
            return false;
        }

        shortcut.DevServerUrl = normalizedDevServer;

        if (!TryValidateOptionalLinkUrl(shortcut.RepoUrl, out error, out var normalizedRepo))
        {
            return false;
        }

        shortcut.RepoUrl = normalizedRepo;

        if (shortcut.OpenDevServerOnLaunch && string.IsNullOrWhiteSpace(shortcut.DevServerUrl))
        {
            error = "Dev server URL is required when open on launch is enabled.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(shortcut.DevServerUrl))
        {
            shortcut.OpenDevServerOnLaunch = false;
        }

        if (!TryValidateCompanionApp(shortcut, out error))
        {
            return false;
        }

        if (!requireDirectoryExists)
        {
            error = string.Empty;
            return true;
        }

        if (!DirectoryExists(shortcut.Directory))
        {
            error = $"Directory not found: {shortcut.Directory}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateForImport(TerminalShortcut shortcut, out string error) =>
        TryValidate(shortcut, requireDirectoryExists: false, out error);

    public static bool TryValidateUniqueName(string name, string? originalName, IShortcutRepository shortcuts, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Name is required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(originalName)
            && name.Equals(originalName, StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        var existing = shortcuts.GetByName(name);
        if (existing is not null)
        {
            error = $"A workspace named '{name}' already exists.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateWtProfile(string? profile, out string error)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            error = string.Empty;
            return true;
        }

        if (profile.Length > MaxWtProfileLength)
        {
            error = $"Terminal profile must be {MaxWtProfileLength} characters or fewer.";
            return false;
        }

        if (profile.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            error = "Terminal profile cannot contain line breaks.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool DirectoryExists(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        if (WslPathResolver.TryParse(directory, out var wslLocation))
        {
            return WslPathResolver.DirectoryExists(wslLocation);
        }

        return Directory.Exists(directory);
    }

    public static bool TryValidateCommand(string? command, out string error)
    {
        if (string.IsNullOrEmpty(command))
        {
            error = string.Empty;
            return true;
        }

        if (command.Length > MaxCommandLength)
        {
            error = $"Command must be {MaxCommandLength} characters or fewer.";
            return false;
        }

        if (command.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            error = "Command cannot contain line breaks.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeDirectory(string directory, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var trimmed = directory.Trim();
        if (WslPathResolver.TryParse(trimmed, out var wslLocation))
        {
            normalized = wslLocation.UncPath ?? trimmed;
            return true;
        }

        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            normalized = trimmed;
            return true;
        }

        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
            error = "Directory path is not valid.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "Directory path is not valid.";
            return false;
        }
        catch (PathTooLongException)
        {
            error = "Directory path is too long.";
            return false;
        }

        if (!Path.IsPathRooted(normalized))
        {
            error = "Directory must be an absolute path.";
            return false;
        }

        return true;
    }

    public static bool TryValidateOptionalLinkUrl(string? url, out string error, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            error = string.Empty;
            return true;
        }

        var trimmed = url.Trim();
        if (trimmed.Length > MaxLinkUrlLength)
        {
            error = $"Link URL must be {MaxLinkUrlLength} characters or fewer.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Link URL must start with http:// or https://.";
            return false;
        }

        normalized = uri.ToString();
        error = string.Empty;
        return true;
    }

    public static bool TryValidateCompanionApp(TerminalShortcut shortcut, out string error)
    {
        // Capture open-on-launch without path before normalize drops empty entries / clears the flag.
        shortcut.CompanionApps ??= [];

        var hasConfiguredListEntry = shortcut.CompanionApps.Any(entry => !string.IsNullOrWhiteSpace(entry.Path));
        if (shortcut.OpenCompanionAppOnLaunch
            && string.IsNullOrWhiteSpace(shortcut.CompanionAppPath)
            && !hasConfiguredListEntry)
        {
            error = "Companion app path is required when open on launch is enabled.";
            return false;
        }

        if (shortcut.CompanionApps.Any(entry =>
                entry.OpenOnLaunch && string.IsNullOrWhiteSpace(entry.Path)))
        {
            error = "Companion app path is required when open on launch is enabled.";
            return false;
        }

        CompanionAppNormalization.NormalizeCompanions(shortcut);

        if (!CompanionAppNormalization.TryValidateCompanions(shortcut, out error))
        {
            return false;
        }

        foreach (var entry in shortcut.CompanionApps)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            var path = entry.Path.Trim();
            if (path.Length > MaxCompanionAppPathLength)
            {
                error = $"Companion app path must be {MaxCompanionAppPathLength} characters or fewer.";
                return false;
            }

            if (!CompanionAppCatalog.TryResolveExecutablePath(path, out var resolvedPath))
            {
                error = $"Companion app not found: {path}";
                return false;
            }

            entry.Path = resolvedPath;

            if (string.IsNullOrWhiteSpace(entry.Arguments))
            {
                entry.Arguments = null;
            }
            else
            {
                var arguments = entry.Arguments.Trim();
                if (arguments.Length > MaxCompanionAppArgumentsLength)
                {
                    error = $"Companion app arguments must be {MaxCompanionAppArgumentsLength} characters or fewer.";
                    return false;
                }

                if (arguments.IndexOfAny(['\r', '\n', '\0']) >= 0)
                {
                    error = "Companion app arguments cannot contain line breaks.";
                    return false;
                }

                entry.Arguments = arguments;
            }
        }

        CompanionAppNormalization.MirrorLegacyFieldsFromPrimary(shortcut);
        error = string.Empty;
        return true;
    }
}
