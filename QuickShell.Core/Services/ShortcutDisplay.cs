using System.Globalization;
using QuickShell.Abstractions;
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

    public static string BuildDirectorySubtitle(TerminalShortcut shortcut, ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return string.Join(" · ", ShortenPath(shortcut.Directory), catalog.GetProfileLabel(shortcut));
    }

    public static string BuildSubtitle(TerminalShortcut shortcut, ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var parts = new List<string> { ShortenPath(shortcut.Directory) };
        var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
        if (enabledLaunches.Count == 1)
        {
            parts.Add(BuildPrimaryLaunchSummary(enabledLaunches[0], catalog));
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

    private static string BuildPrimaryLaunchSummary(WorkspaceEntry launch, ITerminalCatalog catalog)
    {
        var terminal = catalog.GetProfileLabel(new TerminalShortcut
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

    public static string FormatTerminal(string? launchTargetId, ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Resolve(launchTargetId).DisplayName;
    }

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

    private static string CollapseToSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var span = value.AsSpan();
        if (span.IndexOfAny('\r', '\n', '\t') < 0)
        {
            return value.Trim();
        }

        // Bolt: Performance optimization - avoid string.Split() allocations; scan separators directly over the span.
        var builder = new System.Text.StringBuilder(span.Length);
        var first = true;
        var segmentStart = 0;

        for (var i = 0; i <= span.Length; i++)
        {
            var atEnd = i == span.Length;
            if (!atEnd && span[i] != '\r' && span[i] != '\n' && span[i] != '\t')
            {
                continue;
            }

            var part = span[segmentStart..i].Trim();
            if (!part.IsEmpty)
            {
                if (!first)
                {
                    builder.Append(' ');
                }

                builder.Append(part);
                first = false;
            }

            segmentStart = i + 1;
        }

        return builder.ToString();
    }

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

    public static string BuildLaunchEntrySubtitle(WorkspaceEntry entry, ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var parts = new List<string>();

        var taskTitle = TaskTypeCatalog.GetTitle(entry.TaskType);
        if (!string.IsNullOrWhiteSpace(taskTitle))
        {
            parts.Add(taskTitle);
        }

        parts.Add(catalog.GetProfileLabel(new TerminalShortcut
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
