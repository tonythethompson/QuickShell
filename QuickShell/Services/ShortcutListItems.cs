using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services;

internal static class ShortcutListItems
{
    public static ListItem CreateOpen(
        QuickShellPageContext context,
        TerminalShortcut shortcut,
        Action? onChanged = null,
        PinnedMoveVisibility moveVisibility = default,
        bool includeEdit = true,
        Action? onFavoritesReordered = null,
        bool useHomePinContextMenu = false,
        bool? needsRepairOverride = null) =>
        CreateOpen(
            context,
            shortcut,
            presentation: null,
            onChanged,
            moveVisibility,
            includeEdit,
            onFavoritesReordered,
            useHomePinContextMenu,
            needsRepairOverride);

    /// <summary>
    /// Builds a row from cached immutable presentation data when available. Tags are built
    /// for the calling page; per-row <see cref="ListItem.MoreCommands"/> menus are deferred
    /// via <see cref="LazyMoreCommandsListItem"/> until first property read.
    /// </summary>
    public static ListItem CreateOpen(
        QuickShellPageContext context,
        TerminalShortcut shortcut,
        WorkspaceRowPresentation? presentation,
        Action? onChanged = null,
        PinnedMoveVisibility moveVisibility = default,
        bool includeEdit = true,
        Action? onFavoritesReordered = null,
        bool useHomePinContextMenu = false,
        bool? needsRepairOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.Services;
        var settings = context.Settings;
        const bool requireDirectoryExists = false;
        var needsRepair = needsRepairOverride
            ?? (presentation is not null
                ? presentation.State == WorkspaceRowState.NeedsRepair
                : ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists));
        ICommand primaryCommand = needsRepair
            ? new ShortcutFormPage(services, shortcut, onChanged)
            : new OpenTerminalShortcutCommand(shortcut, services);

        var trustPrefix = WorkspaceTrustFeatures.Enabled
            && services.Shortcuts.GetStoredWorkspace(shortcut.Id)?.Security.IsTrusted == false
                ? "Untrusted · "
                : string.Empty;

        ListItem item;
        if (onChanged is not null)
        {
            // Defer Build* until CmdPal reads MoreCommands (selection SlowInitialize).
            Func<IContextItem[]> moreCommandsFactory = needsRepair
                ? () => ShortcutContextCommands.BuildRepairOnly(context, shortcut, onChanged)
                : useHomePinContextMenu
                    ? () => ShortcutContextCommands.BuildForHomePin(
                        context,
                        shortcut,
                        onChanged,
                        needsRepair,
                        moveVisibility,
                        onFavoritesReordered)
                    : () => ShortcutContextCommands.Build(
                        context,
                        shortcut,
                        onChanged,
                        includeEdit,
                        moveVisibility,
                        onFavoritesReordered: onFavoritesReordered,
                        includePageCommands: true,
                        needsRepair: needsRepair);

            item = new LazyMoreCommandsListItem(primaryCommand, moreCommandsFactory);
        }
        else
        {
            item = new ListItem(primaryCommand);
        }

        item.Title = shortcut.Name;
        item.Subtitle = trustPrefix + ShortcutHealth.BuildListSubtitle(shortcut, services.TerminalCatalog, requireDirectoryExists);
        item.Icon = new IconInfo(ShortcutHealth.GetListGlyph(shortcut, services.TerminalLaunchGlyphs, needsRepair));

        var tags = ShortcutDisplayTags.BuildTags(
            shortcut,
            settings.TerminalApplicationId,
            settings.DefaultProfileId,
            services.HealthChecker,
            services.GitOperations);
        if (tags is not null)
        {
            item.Tags = tags;
        }

        return item;
    }

    public static ListItem CreateNewShortcut(CreateShortcutCommand command) =>
        new(command)
        {
            Title = Strings.CreateNewWorkspace_Title,
            Subtitle = Strings.CreateNewWorkspace_Subtitle,
        };
}
