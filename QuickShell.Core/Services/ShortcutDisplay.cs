using System.Globalization;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutDisplay
{
    public static string GetLaunchContextMenuTitle(WorkspaceEntry entry) =>
        GetLaunchContextMenuTitle(entry, siblingLaunches: null);

    public static string GetLaunchContextMenuTitle(
        WorkspaceEntry entry,
        IEnumerable<WorkspaceEntry>? siblingLaunches)
    {
        var command = CollapseToSingleLine(entry.Command);
        if (!string.IsNullOrWhiteSpace(command))
        {
            return command.Trim();
        }

        var launches = siblingLaunches?.ToList() ?? [entry];
        if (AnyLaunchHasCommand(launches))
        {
            return "Open folder only";
        }

        var label = (entry.Label ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return "Open folder";
    }

    public static string BuildDirectorySubtitle(TerminalShortcut shortcut)
    {
        return string.Join(" · ", ShortenPath(shortcut.Directory), TerminalCatalog.GetDisplayName(shortcut));
    }

    public static string BuildSubtitle(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);

        var parts = new List<string> { ShortenPath(shortcut.Directory) };

        var enabledLaunches = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut);
        if (enabledLaunches.Count > 1)
        {
            parts.Add(string.Join(" · ", enabledLaunches.Select(launch => GetLaunchContextMenuTitle(launch, enabledLaunches))));
        }
        else if (enabledLaunches.Count == 1)
        {
            var launch = enabledLaunches[0];
            parts.Add(TerminalCatalog.GetDisplayName(new TerminalShortcut
            {
                Terminal = launch.Terminal,
                WtProfile = launch.WtProfile,
            }));

            if (!string.IsNullOrWhiteSpace(launch.Command))
            {
                parts.Add(launch.Command);
            }
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Abbreviation))
        {
            parts.Add($"home · {shortcut.Abbreviation}");
        }

        if (shortcut.LastUsedUtc is not null)
        {
            parts.Add(FormatRelativeTime(shortcut.LastUsedUtc.Value));
        }

        return string.Join(" · ", parts);
    }

    public static string FormatTerminal(string? launchTargetId) =>
        TerminalCatalog.Resolve(launchTargetId).DisplayName;

    public static string ShortenPathForDisplay(string path)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[userProfile.Length..];
        }

        return path;
    }

    private static bool AnyLaunchHasCommand(IEnumerable<WorkspaceEntry> launches) =>
        launches.Any(launch => !string.IsNullOrWhiteSpace(CollapseToSingleLine(launch.Command)));

    private static string ShortenPath(string path) => ShortenPathForDisplay(path);

    private static string CollapseToSingleLine(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string FormatRelativeTime(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays}d ago";
        }

        return utc.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
    }

    public static string BuildLaunchEntrySubtitle(WorkspaceEntry entry)
    {
        var parts = new List<string>
        {
            TerminalCatalog.GetDisplayName(new TerminalShortcut
            {
                Terminal = entry.Terminal,
                WtProfile = entry.WtProfile,
            }),
        };

        if (!string.IsNullOrWhiteSpace(entry.Command))
        {
            parts.Add(entry.Command);
        }

        if (!entry.IsEnabled)
        {
            parts.Add("disabled");
        }

        return string.Join(" · ", parts);
    }
}
