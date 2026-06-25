using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;
using Windows.System;

namespace QuickShell.Services;

internal static class ShortcutContextCommands
{
    private const int HoverOrderMoveUp = -20;
    private const int HoverOrderMoveDown = -10;
    private const int HoverOrderElevation = 0;
    private const int HoverOrderEdit = 10;
    private const int HoverOrderFavorite = 20;
    private const int HoverOrderDuplicate = 30;
    private const int HoverOrderDelete = 50;

    public static CommandContextItem[] Build(
        TerminalShortcut shortcut,
        Action onChanged,
        QuickShellSettingsManager settings,
        bool includeEdit = true,
        bool showMoveUpInHover = false,
        bool showMoveDownInHover = false)
    {
        var items = new List<CommandContextItem>();

        if (includeEdit)
        {
            var editPage = new ShortcutFormPage(shortcut, onChanged);
            items.Add(WithShortcut(
                editPage,
                ctrl: true,
                alt: false,
                shift: false,
                VirtualKey.E,
                title: editPage.Title,
                showInHoverActions: true,
                hoverOrder: HoverOrderEdit));
        }

        AddElevationContextCommand(items, shortcut, settings);

        var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned);
        items.Add(WithShortcut(
            favoriteCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.P,
            title: favoriteCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderFavorite));

        var duplicateCommand = new DuplicateShortcutCommand(shortcut.Name, onChanged);
        items.Add(WithShortcut(
            duplicateCommand,
            ctrl: true,
            alt: false,
            shift: true,
            VirtualKey.D,
            title: duplicateCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderDuplicate));

        var undoCommand = new UndoShortcutCommand(onChanged);
        items.Add(WithShortcut(
            undoCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.Z,
            title: undoCommand.Name));

        var redoCommand = new RedoShortcutCommand(onChanged);
        items.Add(WithShortcut(
            redoCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.Y,
            title: redoCommand.Name));

        if (shortcut.IsPinned)
        {
            var moveUpCommand = new MoveFavoriteShortcutCommand(shortcut.Name, -1, onChanged);
            items.Add(WithShortcut(
                moveUpCommand,
                ctrl: true,
                alt: true,
                shift: false,
                VirtualKey.Up,
                title: moveUpCommand.Name,
                showInHoverActions: showMoveUpInHover,
                hoverOrder: HoverOrderMoveUp));

            var moveDownCommand = new MoveFavoriteShortcutCommand(shortcut.Name, +1, onChanged);
            items.Add(WithShortcut(
                moveDownCommand,
                ctrl: true,
                alt: true,
                shift: false,
                VirtualKey.Down,
                title: moveDownCommand.Name,
                showInHoverActions: showMoveDownInHover,
                hoverOrder: HoverOrderMoveDown));
        }

        var deleteCommand = new DeleteShortcutCommand(shortcut.Name, onChanged);
        items.Add(WithShortcut(
            deleteCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.Delete,
            title: deleteCommand.Name,
            isCritical: true,
            showInHoverActions: true,
            hoverOrder: HoverOrderDelete));

        return items.ToArray();
    }

    public static CommandContextItem[] BuildForHomePin(
        TerminalShortcut shortcut,
        Action onChanged,
        QuickShellSettingsManager settings)
    {
        var items = new List<CommandContextItem>();

        var editPage = new ShortcutFormPage(shortcut, onChanged);
        items.Add(new CommandContextItem(editPage)
        {
            Title = editPage.Title,
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderEdit,
#endif
            RequestedShortcut = KeyChordHelpers.FromModifiers(
                ctrl: true,
                alt: false,
                shift: false,
                win: false,
                vkey: VirtualKey.E),
        });

        if (!shortcut.RunAsAdmin)
        {
            var adminCommand = new OpenTerminalShortcutCommand(shortcut, settings, runAsAdmin: true);
            items.Add(CreateOpenAsAdminContextItem(adminCommand, showInHoverActions: true));
        }
        else
        {
            var standardCommand = new OpenTerminalShortcutCommand(shortcut, settings, runAsStandard: true);
            items.Add(CreateOpenWithoutAdminContextItem(standardCommand, showInHoverActions: true));
        }

        var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned);
        items.Add(WithShortcut(
            favoriteCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.P,
            title: favoriteCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderFavorite));

        return items.ToArray();
    }

    public static void AddElevationContextCommand(
        IList<CommandContextItem> items,
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        bool insertAtStart = true)
    {
        CommandContextItem contextItem;
        if (shortcut.RunAsAdmin)
        {
            var standardCommand = new OpenTerminalShortcutCommand(shortcut, settings, runAsStandard: true);
            contextItem = CreateOpenWithoutAdminContextItem(standardCommand, showInHoverActions: true);
        }
        else
        {
            var adminCommand = new OpenTerminalShortcutCommand(shortcut, settings, runAsAdmin: true);
            contextItem = CreateOpenAsAdminContextItem(adminCommand, showInHoverActions: true);
        }

        if (insertAtStart)
        {
            items.Insert(0, contextItem);
        }
        else
        {
            items.Add(contextItem);
        }
    }

    public static CommandContextItem CreateOpenAsAdminContextItem(
        OpenTerminalShortcutCommand command,
        bool showInHoverActions = false) =>
        new(command)
        {
            Title = "Open as administrator",
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = showInHoverActions,
            HoverOrder = HoverOrderElevation,
#endif
            RequestedShortcut = KeyChordHelpers.FromModifiers(
                ctrl: true,
                alt: false,
                shift: false,
                win: false,
                vkey: VirtualKey.Enter),
        };

    public static CommandContextItem CreateOpenWithoutAdminContextItem(
        OpenTerminalShortcutCommand command,
        bool showInHoverActions = false) =>
        new(command)
        {
            Title = "Open without administrator",
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = showInHoverActions,
            HoverOrder = HoverOrderElevation,
#endif
            RequestedShortcut = KeyChordHelpers.FromModifiers(
                ctrl: true,
                alt: false,
                shift: true,
                win: false,
                vkey: VirtualKey.Enter),
        };

    private static CommandContextItem WithShortcut(
        ICommand command,
        bool ctrl,
        bool alt,
        bool shift,
        VirtualKey key,
        string title,
        bool isCritical = false,
        bool showInHoverActions = false,
        int hoverOrder = 0) =>
        new(command)
        {
            Title = title,
            IsCritical = isCritical,
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = showInHoverActions,
            HoverOrder = hoverOrder,
#endif
            RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl, alt, shift, win: false, vkey: key),
        };
}
