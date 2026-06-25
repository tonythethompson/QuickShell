using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal sealed partial class QuickShellPage : DynamicListPage, IDisposable
{
    private readonly QuickShellSettingsManager _settings;
    private readonly ImportConflictPage _importConflictPage;
    private readonly PendingShortcutEditPage _pendingShortcutEditPage;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly SearchDebouncer _searchDebouncer;
    private IListItem[] _items = [];
    private string _query = string.Empty;
    private bool _hasShownInitialList;

    public QuickShellPage(
        QuickShellSettingsManager settings,
        ImportConflictPage importConflictPage,
        PendingShortcutEditPage pendingShortcutEditPage,
        CreateShortcutCommand createShortcutCommand)
    {
        _settings = settings;
        _importConflictPage = importConflictPage;
        _pendingShortcutEditPage = pendingShortcutEditPage;
        _createShortcutCommand = createShortcutCommand;
        _searchDebouncer = new SearchDebouncer(ApplyQueryDebounced);
        Id = QuickShellNavigation.HomePageId;
        Icon = new IconInfo("\uE756");
        Title = "Quick Shell";
        Name = "Open";
        PlaceholderText = "Search shortcuts by name, path, or command...";
#if CMDPAL_HOVER_ACTIONS
        HoverActionsMode = HoverActionsMode.Explicit;
        MaxHoverActions = -1;
        HoverActionsVisibility = HoverActionsVisibility.HoverOrSelected;
#endif
        RefreshItems(string.Empty);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (string.IsNullOrEmpty(oldSearch) && string.IsNullOrEmpty(newSearch))
        {
            _hasShownInitialList = false;
            ApplyQuery(string.Empty, immediate: true);
            return;
        }

        if (!_hasShownInitialList)
        {
            _hasShownInitialList = true;
            SetSearchNoUpdate(string.Empty);
            ApplyQuery(string.Empty, immediate: true);
            return;
        }

        ApplyQuery(newSearch);
    }

    public override IListItem[] GetItems() => WithActionBanners(_items);

    private IListItem[] WithActionBanners(IListItem[] items)
    {
        var list = items
            .Where(item => !IsActionBanner(item))
            .ToList();

        var insertAt = 0;

        if (ImportConflictState.Pending is { } pending)
        {
            list.Insert(insertAt++, BuildImportConflictBanner(pending));
        }

        if (ShortcutFormDraftStore.HasPending && ShortcutFormDraftStore.Pending is { } editDraft)
        {
            list.Insert(insertAt, BuildPendingEditBanner(editDraft));
        }

        return list.ToArray();
    }

    private static bool IsActionBanner(IListItem item) =>
        string.Equals(item.Command?.Id, ImportConflictPage.PageId, StringComparison.Ordinal)
        || string.Equals(item.Command?.Id, PendingShortcutEditPage.PageId, StringComparison.Ordinal);

    private ListItem BuildImportConflictBanner(ImportConflictState.PendingImport pending) =>
        new(_importConflictPage)
        {
            Title = "Finish importing shortcuts",
            Subtitle =
                $"{pending.ConflictCount} duplicate name(s) in file — choose merge or replace to continue",
            Icon = new IconInfo("\uE7BA"),
            Tags = [new Tag("Action required")],
        };

    private ListItem BuildPendingEditBanner(PersistedShortcutEditDraft editDraft) =>
        new(_pendingShortcutEditPage)
        {
            Title = "Unsaved shortcut changes",
            Subtitle = $"Editing \"{editDraft.OriginalName}\" — save or discard to continue",
            Icon = new IconInfo("\uE70F"),
            Tags = [new Tag("Action required")],
        };

    public void Reload()
    {
        _searchDebouncer.FlushNow();
        RefreshItems(_query);
    }

    public void Dispose() => _searchDebouncer.Dispose();

    private void ApplyQuery(string query, bool immediate = false)
    {
        var normalized = query ?? string.Empty;
        if (string.Equals(_query, normalized, StringComparison.Ordinal) && _items.Length > 0)
        {
            return;
        }

        if (immediate)
        {
            _searchDebouncer.FlushNow();
            ApplyQueryDebounced(normalized);
            return;
        }

        _searchDebouncer.Schedule(normalized);
    }

    private void ApplyQueryDebounced(string normalized)
    {
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        RefreshItems(normalized);
    }

    private void RefreshItems(string query)
    {
        var shortcuts = ShortcutStore.Search(query).ToArray();
        var pinnedInOrder = shortcuts
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ToList();
        var items = new List<IListItem>(shortcuts.Length + 4);

        foreach (var shortcut in shortcuts)
        {
            items.Add(BuildShortcutItem(shortcut, pinnedInOrder));
        }

        items.Add(ShortcutListItems.CreateNewShortcut(_createShortcutCommand));
        items.Add(new ListItem(new ReloadShortcutsCommand(Reload))
        {
            Title = "Refresh terminals",
            Subtitle = "Reload shortcuts and rediscover installed terminals",
        });
        items.Add(new ListItem(new ExportShortcutsCommand())
        {
            Title = "Export shortcuts",
            Subtitle = "Save shortcuts to a JSON file for backup or sharing",
        });
        items.Add(new ListItem(new ImportShortcutsCommand(Reload))
        {
            Title = "Import shortcuts",
            Subtitle = "Add shortcuts from a JSON file",
        });

        if (!string.IsNullOrWhiteSpace(query) && shortcuts.Length == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "No matching shortcuts",
                Subtitle = "Try a different search",
            });
        }

        _items = items.ToArray();
        RaiseItemsChanged();
    }

    private ListItem BuildShortcutItem(TerminalShortcut shortcut, List<TerminalShortcut> pinnedInOrder)
    {
        var item = ShortcutListItems.CreateOpen(shortcut, _settings);

        var pinnedIndex = -1;
        for (var i = 0; i < pinnedInOrder.Count; i++)
        {
            if (pinnedInOrder[i].Name == shortcut.Name)
            {
                pinnedIndex = i;
                break;
            }
        }
        var showMoveUpInHover = shortcut.IsPinned && pinnedIndex > 0;
        var showMoveDownInHover = shortcut.IsPinned && pinnedIndex >= 0 && pinnedIndex < pinnedInOrder.Count - 1;

        var moreCommands = new List<CommandContextItem>(
            ShortcutContextCommands.Build(
                shortcut,
                Reload,
                _settings,
                showMoveUpInHover: showMoveUpInHover,
                showMoveDownInHover: showMoveDownInHover));

        item.MoreCommands = moreCommands.ToArray();
        return item;
    }
}
