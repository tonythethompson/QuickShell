using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Pages;

internal partial class DiscoverGitReposPage : DynamicListPage
{
    public const string PageId = "com.quickshell.discover-git-repos";

    private readonly Action _onReload;
    private IListItem[] _items = [];
    private string _query = string.Empty;

    public DiscoverGitReposPage(Action onReload)
    {
        _onReload = onReload;
        Id = PageId;
        Icon = new IconInfo(ShortcutGlyphs.Discover);
        Title = "Discover git repos";
        Name = "Discover";
        PlaceholderText = "Filter discovered repositories...";
        GitRepoIndex.Invalidate();
        RefreshItems(string.Empty);
    }

    public override IListItem[] GetItems() => _items;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var normalized = newSearch ?? string.Empty;
        if (string.Equals(_query, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _query = normalized;
        RefreshItems(normalized);
    }

    private void RefreshItems(string query)
    {
        var extraRoots = GitRepoSearchRoots.FromShortcuts(QuickShellRuntimeServices.Shortcuts.GetShortcuts());
        var discovered = GitRepoIndex.GetAll(extraRoots).ToList();
        var shortcuts = QuickShellRuntimeServices.Shortcuts.GetShortcuts();
        var shortcutsByDirectory = DiscoverGitRepoListItems.GroupShortcutsByDirectory(shortcuts);
        var settings = QuickShellRuntimeServices.Settings;

        if (!string.IsNullOrWhiteSpace(query))
        {
            discovered = discovered
                .Where(candidate =>
                    candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || candidate.Directory.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (candidate.RemoteUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var items = DiscoverGitRepoListItems
            .BuildSectionedItems(discovered, _onReload, shortcutsByDirectory, settings)
            .ToList();

        if (items.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = string.IsNullOrWhiteSpace(query) ? "No git repositories found" : "No matching repositories",
                Subtitle = "Try searching Projects, dev, or code under your profile folder",
                Icon = new IconInfo("\uE946"),
            });
        }

        _items = items.ToArray();
        RaiseItemsChanged();
    }
}
