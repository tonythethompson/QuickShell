using QuickShell.Models;

namespace QuickShell.Services;

internal static class TerminalLaunchGlyphs
{
    public static string GetForShortcut(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var launches = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut);
        return launches.Count == 0 ? ShortcutGlyphs.NewWindow : GetForLaunch(launches[0]);
    }

    public static string GetForLaunch(WorkspaceEntry launch)
    {
        var profile = TerminalProfileResolver.ResolveForLaunch(launch);

        if (profile is not null && IsWslProfile(profile))
        {
            if (TryGetInlineProfileIcon(profile, out var inlineIcon))
            {
                return inlineIcon;
            }

            return ShortcutGlyphs.Linux;
        }

        if (TryGetProfileIcon(profile, out var profileIcon))
        {
            return profileIcon;
        }

        var taskGlyph = TaskTypeCatalog.GetGlyph(launch.TaskType);
        if (taskGlyph is not null)
        {
            return taskGlyph;
        }

        return GetFallbackGlyph(launch, profile);
    }

    private static bool TryGetProfileIcon(WtProfileInfo? profile, out string icon)
    {
        icon = string.Empty;

        if (profile is null)
        {
            return false;
        }

        var resolved = TerminalProfileIconResolver.ResolveEffectiveIcon(profile);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        icon = resolved;
        return true;
    }

    /// <summary>
    /// Emoji or Segoe glyph stored directly on the profile (not a PNG path).
    /// </summary>
    private static bool TryGetInlineProfileIcon(WtProfileInfo profile, out string icon)
    {
        icon = string.Empty;
        var resolved = TerminalProfileIconResolver.ResolveEffectiveIcon(profile);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        if (!TerminalProfileIconResolver.IsInlineGlyphIcon(resolved))
        {
            return false;
        }

        icon = resolved;
        return true;
    }

    internal static bool IsWslProfile(WtProfileInfo profile)
    {
        if (profile.Commandline?.Contains("wsl.exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (profile.ProfileSource?.Contains("WSL", StringComparison.OrdinalIgnoreCase) == true
            || profile.ProfileSource?.Contains("Windows.Subsystem.Linux", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return IsLinuxDistroName(profile.Name);
    }

    private static bool IsWslProfile(string? terminal, string? profileName)
    {
        if (terminal?.Equals("wsl", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return IsLinuxDistroName(profileName);
    }

    private static string GetFallbackGlyph(WorkspaceEntry launch, WtProfileInfo? profile)
    {
        if (profile is not null && IsWslProfile(profile))
        {
            return ShortcutGlyphs.Linux;
        }

        var terminal = (launch.Terminal ?? "default").Trim().ToLowerInvariant();
        if (IsWslProfile(terminal, launch.WtProfile))
        {
            return ShortcutGlyphs.Linux;
        }

        return terminal switch
        {
            "pwsh" or "powershell7" => ShortcutGlyphs.PowerShell,
            "powershell" => ShortcutGlyphs.PowerShell,
            "cmd" => ShortcutGlyphs.CommandPrompt,
            "wt" or "it" or "windows-terminal" or "intelligent-terminal" => ShortcutGlyphs.Terminal,
            "wsl" => ShortcutGlyphs.Linux,
            _ => ShortcutGlyphs.NewWindow,
        };
    }

    private static bool IsLinuxDistroName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)
            || value.Contains("debian", StringComparison.OrdinalIgnoreCase)
            || value.Contains("fedora", StringComparison.OrdinalIgnoreCase)
            || value.Contains("linux", StringComparison.OrdinalIgnoreCase)
            || value.Contains("wsl", StringComparison.OrdinalIgnoreCase)
            || value.Contains("alpine", StringComparison.OrdinalIgnoreCase)
            || value.Contains("arch", StringComparison.OrdinalIgnoreCase)
            || value.Contains("kali", StringComparison.OrdinalIgnoreCase)
            || value.Contains("opensuse", StringComparison.OrdinalIgnoreCase)
            || value.Contains("suse", StringComparison.OrdinalIgnoreCase)
            || value.Contains("mint", StringComparison.OrdinalIgnoreCase)
            || value.Contains("gentoo", StringComparison.OrdinalIgnoreCase);
    }
}
