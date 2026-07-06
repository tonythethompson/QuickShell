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
            var taskTitle = TaskTypeCatalog.GetTitle(entry.TaskType);
            return string.IsNullOrWhiteSpace(taskTitle)
                ? command.Trim()
                : $"{taskTitle} — {command.Trim()}";
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
        return string.Join(" · ", ShortenPath(shortcut.Directory), TerminalCatalog.GetProfileLabel(shortcut));
    }

    public static string BuildSubtitle(TerminalShortcut shortcut)
    {
        var parts = new List<string> { ShortenPath(shortcut.Directory) };
        var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
        if (enabledLaunches.Count == 1)
        {
            parts.Add(BuildPrimaryLaunchSummary(enabledLaunches[0]));
        }
        else if (enabledLaunches.Count > 1)
        {
            parts.Add($"{enabledLaunches.Count} launches");
        }

        if (shortcut.LastUsedUtc is not null)
        {
            parts.Add(FormatRelativeTime(shortcut.LastUsedUtc.Value));
        }

        return string.Join(" · ", parts);
    }

    private static string BuildPrimaryLaunchSummary(WorkspaceEntry launch)
    {
        var terminal = TerminalCatalog.GetProfileLabel(new TerminalShortcut
        {
            Terminal = launch.Terminal,
            WtProfile = launch.WtProfile,
        });
        var command = CollapseToSingleLine(launch.Command);
        if (!string.IsNullOrWhiteSpace(command))
        {
            return $"{terminal}: {Truncate(command.Trim(), 56)}";
        }

        return string.IsNullOrWhiteSpace(launch.Label)
            ? terminal
            : $"{terminal}: {launch.Label.Trim()}";
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

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";

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
        var parts = new List<string>();

        var taskTitle = TaskTypeCatalog.GetTitle(entry.TaskType);
        if (!string.IsNullOrWhiteSpace(taskTitle))
        {
            parts.Add(taskTitle);
        }

        parts.Add(TerminalCatalog.GetProfileLabel(new TerminalShortcut
        {
            Terminal = entry.Terminal,
            WtProfile = entry.WtProfile,
        }));

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
