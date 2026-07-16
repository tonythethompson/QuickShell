using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;
using System.Text.Json;

namespace QuickShell.Services;

internal static class DiscoverGitRepoListItems
{
    public static string NotSavedSectionTitle => Strings.Section_NotSavedYet;

    public static string SavedSectionTitle => Strings.Section_AlreadyWorkspaces;

    public static IEnumerable<IListItem> BuildSectionedItems(
        IEnumerable<GitRepoCandidate> discovered,
        Action onSaved,
        IReadOnlyDictionary<string, List<TerminalShortcut>> shortcutsByDirectory,
        QuickShellSettingsManager? settings,
        IDictionary<string, ListItem>? itemCache = null,
        IQuickShellServices? services = null)
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

        var usedKeys = itemCache is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (unsaved.Count > 0)
        {
            foreach (var item in SectionListItems.InSection(
                         NotSavedSectionTitle,
                         unsaved
                             .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(candidate => CreateNew(candidate, onSaved, itemCache: itemCache, usedKeys: usedKeys, services: services))))
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
                                 settings,
                                 itemCache: itemCache,
                                 usedKeys: usedKeys,
                                 services: services))))
            {
                yield return item;
            }
        }

        if (itemCache is not null && usedKeys is not null)
        {
            foreach (var stale in itemCache.Keys.Where(key => !usedKeys.Contains(key)).ToList())
            {
                itemCache.Remove(stale);
            }
        }
    }

    public static ListItem CreateNew(
        GitRepoCandidate candidate,
        Action onSaved,
        string? title = null,
        IDictionary<string, ListItem>? itemCache = null,
        ISet<string>? usedKeys = null,
        IQuickShellServices? services = null)
    {
        var requiredServices = services ?? throw new InvalidOperationException("IQuickShellServices is required.");
        var cacheKey = BuildCacheKey("new", candidate);
        usedKeys?.Add(cacheKey);

        if (itemCache is not null && itemCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var item = new ListItem(new CreateShortcutCommand(onSaved, WorkspaceSeedFactory.FromGitRepo(candidate), requiredServices))
        {
            Title = title ?? candidate.Name,
            Subtitle = BuildSubtitleForNew(candidate),
            Icon = new IconInfo(ShortcutGlyphs.Add),
            MoreCommands = BuildDirectoryCommands(candidate.Directory),
        };

        if (itemCache is not null)
        {
            itemCache[cacheKey] = item;
        }

        return item;
    }

    public static ListItem CreateSaved(
        GitRepoCandidate candidate,
        Action onSaved,
        IReadOnlyList<TerminalShortcut> matchingShortcuts,
        QuickShellSettingsManager? settings = null,
        string? title = null,
        IDictionary<string, ListItem>? itemCache = null,
        ISet<string>? usedKeys = null,
        IQuickShellServices? services = null)
    {
        var requiredServices = services ?? throw new InvalidOperationException("IQuickShellServices is required.");
        var cacheKey = BuildCacheKey("saved", candidate, matchingShortcuts);
        usedKeys?.Add(cacheKey);

        if (itemCache is not null && itemCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var moreCommands = settings is not null && matchingShortcuts.Count > 0
            ? BuildSavedWorkspaceCommands(candidate.Directory, matchingShortcuts, settings, onSaved, services)
            : BuildDirectoryCommands(candidate.Directory);

        var item = new ListItem(new CreateShortcutCommand(onSaved, WorkspaceSeedFactory.FromGitRepo(candidate), requiredServices))
        {
            Title = title ?? candidate.Name,
            Subtitle = BuildSubtitleForSaved(candidate, matchingShortcuts),
            Icon = new IconInfo(ShortcutGlyphs.Saved),
            MoreCommands = moreCommands,
        };

        if (itemCache is not null)
        {
            itemCache[cacheKey] = item;
        }

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
        var parts = new List<string> { Strings.AddAnotherWorkspace };

        switch (matchingShortcuts.Count)
        {
            case 1:
                parts.Add(Strings.SavedAsFormat(matchingShortcuts[0].Name));
                break;
            case > 1:
                parts.Add(Strings.WorkspacesCountFormat(matchingShortcuts.Count));
                parts.Add(Strings.RightClickToOpenOrEdit);
                break;
        }

        parts.Add(ShortcutDisplay.ShortenPathForDisplay(candidate.Directory));
        AppendClassification(parts, candidate);
        AppendRemote(parts, candidate);
        return string.Join(" · ", parts);
    }

    private static string JoinPathAndRemote(GitRepoCandidate candidate)
    {
        var parts = new List<string> { ShortcutDisplay.ShortenPathForDisplay(candidate.Directory) };
        AppendClassification(parts, candidate);
        AppendRemote(parts, candidate);
        return string.Join(" · ", parts);
    }

    private static string BuildCacheKey(
        string kind,
        GitRepoCandidate candidate,
        IReadOnlyList<TerminalShortcut>? matchingShortcuts = null)
    {
        var classification = candidate.Classification;
        var candidateSignature = string.Join(
            "\u001F",
            candidate.Directory,
            candidate.Name,
            candidate.RemoteUrl ?? string.Empty,
            classification.Stacks.ToString(),
            string.Join("\u001E", classification.Labels),
            string.Join("\u001E", classification.NodeScripts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}\u001D{pair.Value}")),
            string.Join("\u001E", classification.DenoTasks.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}\u001D{pair.Value}")),
            string.Join("\u001E", classification.DotNetProjects),
            string.Join("\u001E", classification.RunnableDotNetProjects),
            string.Join("\u001E", classification.MakeTargets),
            string.Join("\u001E", classification.JustRecipes),
            string.Join("\u001E", classification.TaskfileTasks),
            string.Join("\u001E", classification.VsCodeTasks.Select(task => $"{task.Label}\u001D{task.Command}")),
            classification.HasSpringBoot.ToString(),
            classification.HasForemanRunner.ToString());

        return matchingShortcuts is null
            ? $"{kind}:{candidateSignature}"
            : $"{kind}:{candidateSignature}\u001F{JsonSerializer.Serialize(matchingShortcuts, QuickShellJsonContext.Default.ListTerminalShortcut)}";
    }

    private static void AppendClassification(List<string> parts, GitRepoCandidate candidate)
    {
        if (candidate.Classification.Labels.Count > 0)
        {
            parts.Add(string.Join(", ", candidate.Classification.Labels.Take(5)));
        }
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
            Title = Strings.OpenDirectory,
            Icon = new IconInfo("\uE838"),
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = 10,
#endif
        },
    ];

    private static CommandContextItem[] BuildSavedWorkspaceCommands(
        string directory,
        IReadOnlyList<TerminalShortcut> matchingShortcuts,
        QuickShellSettingsManager settings,
        Action onChanged,
        IQuickShellServices? services = null)
    {
        var items = new List<CommandContextItem>(BuildDirectoryCommands(directory));
#if CMDPAL_HOVER_ACTIONS
        var hoverOrder = 20;
#endif
        foreach (var shortcut in matchingShortcuts)
        {
            const bool requireDirectoryExists = false;
            var needsRepair = ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists);
            if (needsRepair)
            {
                items.Add(new CommandContextItem(new ShortcutFormPage(shortcut, onChanged, services: services))
                {
                    Title = shortcut.Name,
                    Subtitle = Strings.RepairWorkspace,
                    Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut, needsRepair)),
#if CMDPAL_HOVER_ACTIONS
                    ShowInHoverActions = true,
                    HoverOrder = hoverOrder++,
#endif
                });
                continue;
            }

            items.Add(new CommandContextItem(new OpenTerminalShortcutCommand(shortcut, settings, services: services))
            {
                Title = shortcut.Name,
                Subtitle = Strings.OpenWorkspace,
                Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut, needsRepair)),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = hoverOrder++,
#endif
            });

            items.Add(new CommandContextItem(new ShortcutFormPage(shortcut, onChanged, services: services))
            {
                Title = Strings.EditNamedFormat(shortcut.Name),
                Icon = new IconInfo("\uE70F"),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = hoverOrder++,
#endif
            });
        }

        return items.ToArray();
    }
}
