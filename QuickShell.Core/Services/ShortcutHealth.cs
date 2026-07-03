using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutHealth
{
    public static bool NeedsRepair(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);

        if (string.IsNullOrWhiteSpace(shortcut.Name) || string.IsNullOrWhiteSpace(shortcut.Directory))
        {
            return true;
        }

        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out _, out _))
        {
            return true;
        }

        if (!ShortcutValidation.DirectoryExists(shortcut.Directory))
        {
            return true;
        }

        return !ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out _);
    }

    public static string GetListGlyph(TerminalShortcut shortcut)
    {
        if (NeedsRepair(shortcut))
        {
            return ShortcutGlyphs.IncidentTriangle;
        }

        if (shortcut.RunAsAdmin)
        {
            return ShortcutGlyphs.AdminLaunch;
        }

        return TerminalLaunchGlyphs.GetForShortcut(shortcut);
    }

    public static string BuildListSubtitle(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);

        if (string.IsNullOrWhiteSpace(shortcut.Directory))
        {
            return "Choose workspace folder · fix in edit";
        }

        if (!ShortcutValidation.TryNormalizeDirectory(shortcut.Directory, out _, out _))
        {
            return "Invalid folder path · fix in edit";
        }

        if (!ShortcutValidation.DirectoryExists(shortcut.Directory))
        {
            return $"Folder not found · {ShortcutDisplay.ShortenPathForDisplay(shortcut.Directory)}";
        }

        if (!ShortcutLaunchNormalization.TryValidateLaunches(shortcut, out var launchError)
            && !string.IsNullOrWhiteSpace(launchError))
        {
            return $"Invalid workspace · {launchError}";
        }

        if (shortcut.OpenCompanionAppOnLaunch
            && !string.IsNullOrWhiteSpace(shortcut.CompanionAppPath)
            && !CompanionAppCatalog.TryResolveExecutablePath(shortcut.CompanionAppPath, out _))
        {
            return $"Companion app missing · {ShortcutDisplay.BuildSubtitle(shortcut)}";
        }

        return ShortcutDisplay.BuildSubtitle(shortcut);
    }
}
