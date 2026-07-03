using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services;

internal static class DiscoverGitRepoListItems
{
    public const string NotSavedSectionTitle = "Not saved yet";

    public const string SavedSectionTitle = "Already workspaces";

    public static IEnumerable<IListItem> BuildSectionedItems(
        IEnumerable<GitRepoCandidate> discovered,
        Action onSaved,
        IReadOnlyDictionary<string, List<TerminalShortcut>> shortcutsByDirectory,
        QuickShellSettingsManager? settings)
    {
        var unsaved = new List<GitRepoCandidate>();
        var saved = new List<(GitRepoCandidate Candidate, IReadOnlyList<TerminalShortcut> Shortcuts)>();

        foreach (var candidate in discovered)
        {
            var matchingShortcuts = GetMatchingShortcuts(candidate, shortcutsByDirectory);
            if (matchingShortcuts.Count == 0)
            {
                unsaved.Add(candidate);
            }
            else
            {
                saved.Add((candidate, matchingShortcuts));
            }
        }

        if (unsaved.Count > 0)
        {
            foreach (var item in SectionListItems.InSection(
                         NotSavedSectionTitle,
                         unsaved
                             .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(candidate => CreateNew(candidate, onSaved))))
            {
                yield return item;
            }
        }

        if (saved.Count > 0)
        {
            foreach (var item in SectionListItems.InSection(
                         SavedSectionTitle,
                         saved
                             .OrderBy(entry => entry.Candidate.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(entry => CreateSaved(
                                 entry.Candidate,
                                 onSaved,
                                 entry.Shortcuts,
                                 settings))))
            {
                yield return item;
            }
        }
    }

    public static ListItem CreateNew(
        GitRepoCandidate candidate,
        Action onSaved,
        string? title = null)
    {
        var item = new ListItem(new CreateShortcutCommand(onSaved, WorkspaceSeedFactory.FromGitRepo(candidate)))
        {
            Title = title ?? candidate.Name,
            Subtitle = BuildSubtitleForNew(candidate),
            Icon = new IconInfo(ShortcutGlyphs.Add),
            MoreCommands = BuildDirectoryCommands(candidate.Directory),
        };

        return item;
    }

    public static ListItem CreateSaved(
        GitRepoCandidate candidate,
        Action onSaved,
        IReadOnlyList<TerminalShortcut> matchingShortcuts,
        QuickShellSettingsManager? settings = null,
        string? title = null)
    {
        var item = new ListItem(new CreateShortcutCommand(onSaved, WorkspaceSeedFactory.FromGitRepo(candidate)))
        {
            Title = title ?? candidate.Name,
            Subtitle = BuildSubtitleForSaved(candidate, matchingShortcuts),
            Icon = new IconInfo(ShortcutGlyphs.Saved),
        };

        item.MoreCommands = settings is not null && matchingShortcuts.Count > 0
            ? BuildSavedWorkspaceCommands(candidate.Directory, matchingShortcuts, settings, onSaved)
            : BuildDirectoryCommands(candidate.Directory);

        return item;
    }

    public static IReadOnlyDictionary<string, List<TerminalShortcut>> GroupShortcutsByDirectory(
        IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts
            .GroupBy(shortcut => shortcut.Directory, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TerminalShortcut> GetMatchingShortcuts(
        GitRepoCandidate candidate,
        IReadOnlyDictionary<string, List<TerminalShortcut>> shortcutsByDirectory) =>
        shortcutsByDirectory.TryGetValue(candidate.Directory, out var matches)
            ? matches
            : [];

    internal static string BuildSubtitleForNew(GitRepoCandidate candidate) =>
        JoinPathAndRemote(candidate);

    internal static string BuildSubtitleForSaved(
        GitRepoCandidate candidate,
        IReadOnlyList<TerminalShortcut> matchingShortcuts)
    {
        var parts = new List<string> { "Add another workspace" };

        switch (matchingShortcuts.Count)
        {
            case 1:
                parts.Add($"Saved as {matchingShortcuts[0].Name}");
                break;
            case > 1:
                parts.Add($"{matchingShortcuts.Count} workspaces");
                parts.Add("Right-click to open or edit");
                break;
        }

        parts.Add(ShortcutDisplay.ShortenPathForDisplay(candidate.Directory));
        AppendRemote(parts, candidate);
        return string.Join(" · ", parts);
    }

    private static string JoinPathAndRemote(GitRepoCandidate candidate)
    {
        var parts = new List<string> { ShortcutDisplay.ShortenPathForDisplay(candidate.Directory) };
        AppendRemote(parts, candidate);
        return string.Join(" · ", parts);
    }

    private static void AppendRemote(List<string> parts, GitRepoCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.RemoteUrl))
        {
            parts.Add(candidate.RemoteUrl);
        }
    }

    private static CommandContextItem[] BuildDirectoryCommands(string directory) =>
    [
        new(new OpenDirectoryInExplorerCommand(directory))
        {
            Title = "Open directory",
            Icon = new IconInfo("\uE838"),
        },
    ];

    private static CommandContextItem[] BuildSavedWorkspaceCommands(
        string directory,
        IReadOnlyList<TerminalShortcut> matchingShortcuts,
        QuickShellSettingsManager settings,
        Action onChanged)
    {
        var items = new List<CommandContextItem>(BuildDirectoryCommands(directory));
        foreach (var shortcut in matchingShortcuts)
        {
            if (ShortcutHealth.NeedsRepair(shortcut))
            {
                items.Add(new CommandContextItem(new ShortcutFormPage(shortcut, onChanged))
                {
                    Title = shortcut.Name,
                    Subtitle = "Repair workspace",
                    Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut)),
                });
                continue;
            }

            items.Add(new CommandContextItem(new OpenTerminalShortcutCommand(shortcut, settings))
            {
                Title = shortcut.Name,
                Subtitle = "Open workspace",
                Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut)),
            });

            items.Add(new CommandContextItem(new ShortcutFormPage(shortcut, onChanged))
            {
                Title = $"Edit {shortcut.Name}",
                Icon = new IconInfo("\uE70F"),
            });
        }

        return items.ToArray();
    }
}
