using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutValidation
{
    public const int MaxNameLength = 120;
    public const int MaxAbbreviationLength = 32;
    public const int MaxDirectoryLength = 1024;
    public const int MaxCommandLength = 4000;
    public const int MaxWtProfileLength = 120;
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
            error = $"Abbreviation must be {MaxAbbreviationLength} characters or fewer.";
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

    public static bool TryValidateUniqueName(string name, string? originalName, out string error)
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

        var existing = ShortcutStore.GetByName(name);
        if (existing is not null)
        {
            error = $"A shortcut named '{name}' already exists.";
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
        catch (Exception ex)
        {
            error = $"Directory is not valid: {ex.Message}";
            return false;
        }

        if (!Path.IsPathRooted(normalized))
        {
            error = "Directory must be an absolute path.";
            return false;
        }

        return true;
    }
}
