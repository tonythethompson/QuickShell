using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutHealth
{
    public static bool WouldNeedRepair(TerminalShortcut shortcut, bool requireDirectoryExists = true)
    {
        if (string.IsNullOrWhiteSpace(shortcut.Name) || string.IsNullOrWhiteSpace(shortcut.Directory))
        {
            return true;
        }

        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out _, out _))
        {
            return true;
        }

        if (requireDirectoryExists && !ShortcutValidation.DirectoryExists(shortcut.Directory))
        {
            return true;
        }

        if (shortcut.Launches is null || shortcut.Launches.Count == 0)
        {
            return false;
        }

        return !ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out _);
    }

    public static string GetListGlyph(TerminalShortcut shortcut, bool? needsRepair = null)
    {
        if (needsRepair ?? WouldNeedRepair(shortcut))
        {
            return ShortcutGlyphs.IncidentTriangle;
        }

        if (shortcut.RunAsAdmin)
        {
            return ShortcutGlyphs.AdminLaunch;
        }

        return TerminalLaunchGlyphs.GetForShortcut(shortcut);
    }

    public static string BuildListSubtitle(TerminalShortcut shortcut, bool requireDirectoryExists = true)
    {
        if (string.IsNullOrWhiteSpace(shortcut.Directory))
        {
            return "Choose workspace folder · fix in edit";
        }

        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out _, out _))
        {
            return "Invalid folder path · fix in edit";
        }

        if (requireDirectoryExists && !ShortcutValidation.DirectoryExists(shortcut.Directory))
        {
            return $"Folder not found · {ShortcutDisplay.ShortenPathForDisplay(shortcut.Directory)}";
        }

        if (shortcut.Launches is { Count: > 0 }
            && !ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out var launchError)
            && !string.IsNullOrWhiteSpace(launchError))
        {
            return $"Invalid workspace · {launchError}";
        }

        if (requireDirectoryExists
            && shortcut.OpenCompanionAppOnLaunch
            && !string.IsNullOrWhiteSpace(shortcut.CompanionAppPath)
            && !CompanionAppCatalog.TryResolveExecutablePath(shortcut.CompanionAppPath, out _))
        {
            return $"Companion app missing · {ShortcutDisplay.BuildSubtitle(shortcut)}";
        }

        return ShortcutDisplay.BuildSubtitle(shortcut);
    }
}
