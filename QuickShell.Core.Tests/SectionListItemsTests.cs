using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class SectionListItemsTests
{
    [Fact]
    public void InSection_InsertsSeparatorHeaderBeforeItems()
    {
        var items = SectionListItems.InSection(
            "Favorites",
            [CreateWorkspaceItem("Pinned")]).ToList();

        Assert.Equal(2, items.Count);
        AssertSeparator(items[0], "Favorites");
        Assert.IsType<ListItem>(items[1]);
    }

    [Fact]
    public void InSection_EmptyItems_DoesNotInsertHeader()
    {
        var items = SectionListItems.InSection("Favorites", []).ToList();
        Assert.Empty(items);
    }

    [Fact]
    public void InSection_BlankTitle_ReturnsItemsWithoutHeader()
    {
        var items = SectionListItems.InSection(
            "  ",
            [CreateWorkspaceItem("Only")]).ToList();

        Assert.Single(items);
        Assert.IsType<ListItem>(items[0]);
    }

    [Fact]
    public void CreateHeader_UsesCmdPalSeparatorContract()
    {
        var header = SectionListItems.CreateHeader("Recent");

        Assert.Null(header.Command);
        Assert.Equal("Recent", header.Section);
        Assert.Equal("Recent", header.Title);
    }

    private static ListItem CreateWorkspaceItem(string title) =>
        new(new NoOpCommand())
        {
            Title = title,
        };

    private static void AssertSeparator(IListItem item, string expectedTitle)
    {
        var separator = Assert.IsType<Separator>(item);
        Assert.Equal(expectedTitle, separator.Section);
        Assert.Null(separator.Command);
    }
}

public sealed class ShortcutLayoutDisplayTests
{
    [Fact]
    public void BuildListItems_WithPinnedAndUnpinned_EmitsFavoritesThenWorkspacesHeaders()
    {
        var pinned = CreateShortcut("Pinned", isPinned: true, pinOrder: 0);
        var workspace = CreateShortcut("Workspace", isPinned: false);

        var layout = new List<ShortcutLayoutEntry>
        {
            ShortcutLayoutEntry.FromShortcut(pinned),
            ShortcutLayoutEntry.FromShortcut(workspace),
        };

        var items = ShortcutLayoutDisplay.BuildListItems(
            layout,
            shortcut => CreateWorkspaceItem(shortcut.Name)).ToList();

        Assert.Equal(4, items.Count);
        AssertSeparator(items[0], ShortcutLayoutDisplay.FavoritesSectionTitle);
        Assert.Equal("Pinned", ((ListItem)items[1]).Title);
        AssertSeparator(items[2], ShortcutLayoutDisplay.ShortcutsSectionTitle);
        Assert.Equal("Workspace", ((ListItem)items[3]).Title);
    }

    [Fact]
    public void BuildListItems_WithLayoutSeparator_EmitsCustomSectionHeader()
    {
        var alpha = CreateShortcut("Alpha", isPinned: false);
        var beta = CreateShortcut("Beta", isPinned: false);

        var layout = new List<ShortcutLayoutEntry>
        {
            ShortcutLayoutEntry.FromShortcut(alpha),
            ShortcutLayoutEntry.FromSeparator("Client repos"),
            ShortcutLayoutEntry.FromShortcut(beta),
        };

        var items = ShortcutLayoutDisplay.BuildListItems(
            layout,
            shortcut => CreateWorkspaceItem(shortcut.Name)).ToList();

        Assert.Equal(3, items.Count);
        Assert.Equal("Alpha", ((ListItem)items[0]).Title);
        AssertSeparator(items[1], "Client repos");
        Assert.Equal("Beta", ((ListItem)items[2]).Title);
    }

    [Fact]
    public void BuildWorkspaceItems_AfterFavoritesAndRecents_StillShowsWorkspacesHeader()
    {
        var pinned = CreateShortcut("Pinned", isPinned: true, pinOrder: 0);
        var workspace = CreateShortcut("Workspace", isPinned: false);

        var layout = new List<ShortcutLayoutEntry>
        {
            ShortcutLayoutEntry.FromShortcut(pinned),
            ShortcutLayoutEntry.FromShortcut(workspace),
        };

        var items = ShortcutLayoutDisplay.BuildWorkspaceItems(
            layout,
            shortcut => CreateWorkspaceItem(shortcut.Name),
            excludeShortcutIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            showDefaultWorkspacesHeader: true).ToList();

        Assert.Equal(2, items.Count);
        AssertSeparator(items[0], ShortcutLayoutDisplay.ShortcutsSectionTitle);
        Assert.Equal("Workspace", ((ListItem)items[1]).Title);
    }

    [Fact]
    public void HomeSectionOrder_FavoritesBeforeRecentsBeforeWorkspaces()
    {
        var pinned = CreateShortcut("Pinned", isPinned: true, pinOrder: 0);
        var recent = CreateShortcut("Recent", isPinned: false);
        recent.LastUsedUtc = DateTime.UtcNow;
        var workspace = CreateShortcut("Workspace", isPinned: false);

        var layout = new List<ShortcutLayoutEntry>
        {
            ShortcutLayoutEntry.FromShortcut(pinned),
            ShortcutLayoutEntry.FromShortcut(recent),
            ShortcutLayoutEntry.FromShortcut(workspace),
        };

        var recentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { recent.Id };
        var items = new List<IListItem>();
        items.AddRange(ShortcutLayoutDisplay.BuildFavoriteItems(layout, s => CreateWorkspaceItem(s.Name)));
        items.AddRange(SectionListItems.InSection(
            ShortcutRecents.SectionTitle,
            [CreateWorkspaceItem(recent.Name)]));
        items.AddRange(ShortcutLayoutDisplay.BuildWorkspaceItems(
            layout,
            s => CreateWorkspaceItem(s.Name),
            recentIds,
            showDefaultWorkspacesHeader: true));

        Assert.Equal(6, items.Count);
        AssertSeparator(items[0], ShortcutLayoutDisplay.FavoritesSectionTitle);
        Assert.Equal("Pinned", ((ListItem)items[1]).Title);
        AssertSeparator(items[2], ShortcutRecents.SectionTitle);
        Assert.Equal("Recent", ((ListItem)items[3]).Title);
        AssertSeparator(items[4], ShortcutLayoutDisplay.ShortcutsSectionTitle);
        Assert.Equal("Workspace", ((ListItem)items[5]).Title);
    }

    [Fact]
    public void BuildListItems_ExcludesRecentShortcutIds()
    {
        var recent = CreateShortcut("Recent", isPinned: false);
        recent.Id = "recent-id";

        var layout = new List<ShortcutLayoutEntry>
        {
            ShortcutLayoutEntry.FromShortcut(recent),
        };

        var items = ShortcutLayoutDisplay.BuildListItems(
            layout,
            shortcut => CreateWorkspaceItem(shortcut.Name),
            excludeShortcutIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { recent.Id }).ToList();

        Assert.Empty(items);
    }

    private static TerminalShortcut CreateShortcut(string name, bool isPinned, int? pinOrder = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Directory = @"C:\Projects\" + name,
            IsPinned = isPinned,
            PinOrder = pinOrder,
        };

    private static ListItem CreateWorkspaceItem(string title) =>
        new(new NoOpCommand())
        {
            Title = title,
        };

    private static void AssertSeparator(IListItem item, string expectedTitle)
    {
        var separator = Assert.IsType<Separator>(item);
        Assert.Equal(expectedTitle, separator.Section);
        Assert.Null(separator.Command);
    }
}
