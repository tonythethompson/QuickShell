using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Pages;
using Windows.System;
namespace QuickShell.Services;

internal static class ShortcutContextCommands
{
    private const int HoverOrderMoveToTop = -25;
    private const int HoverOrderMoveUp = -20;
    private const int HoverOrderMoveDown = -10;
    private const int HoverOrderMoveToBottom = -5;
    private const int HoverOrderUndo = -2;
    private const int HoverOrderRedo = -1;
    private const int HoverOrderElevation = 0;
    private const int HoverOrderOpenExplorer = 1;
    private const int HoverOrderCopyPath = 2;
    private const int HoverOrderDevServer = 3;
    private const int HoverOrderRepo = 4;
    private const int HoverOrderCompanionApp = 5;
    private const int HoverOrderStatus = 6;
    private const int HoverOrderCopyDiagnostics = 7;
    private const int HoverOrderEdit = 8;
    private const int HoverOrderCreate = 15;
    private const int HoverOrderFavorite = 20;
    private const int HoverOrderDuplicate = 30;
    private const int HoverOrderDelete = 50;

    public static CommandContextItem CreateSettingsItem(QuickShellSettingsManager settings) =>
        new(settings.SettingsPage)
        {
            Title = QuickShellBrand.SettingsTitle,
            Icon = new IconInfo(""),
        };

    public static CommandContextItem[] Build(
        TerminalShortcut shortcut,
        Action onChanged,
        QuickShellSettingsManager settings,
        CreateShortcutCommand? createShortcutCommand = null,
        bool includeEdit = true,
        PinnedMoveVisibility moveVisibility = default)
    {
        // Skip DirectoryExists here — list open already uses requireDirectoryExists:false
        // for primary rows; disk checks on every context menu make leave/reload laggy.
        if (ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
        {
            return BuildRepairOnly(shortcut, onChanged, settings);
        }

        var items = new List<CommandContextItem>();

        // Open
        var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
        if (enabledLaunches.Count > 1)
        {
            foreach (var launch in enabledLaunches)
            {
                items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(shortcut, launch, settings))
                {
                    Title = ShortcutDisplay.GetLaunchContextMenuTitle(launch, enabledLaunches),
                    Icon = new IconInfo(TerminalLaunchGlyphs.GetForLaunch(launch)),
                });
            }
        }

        AddElevationContextCommand(items, shortcut, settings);

        // Workspace
        AddFolderAndLinkCommands(items, shortcut);

        // Status…
        AddStatusCommand(items, shortcut, settings, onChanged);
        AddLaunchDiagnosticsCommand(items);

        // Manage
        if (includeEdit)
        {
            items.Add(WithShortcut(
                new ShortcutFormPage(shortcut, onChanged),
                ctrl: true,
                alt: false,
                shift: false,
                VirtualKey.E,
                title: Strings.Menu_Edit,
                showInHoverActions: true,
                hoverOrder: HoverOrderEdit));
        }

        var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned);
        items.Add(WithShortcut(
            favoriteCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.F,
            title: favoriteCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderFavorite));

        if (shortcut.IsPinned)
        {
            AddPinnedMoveCommands(items, shortcut, onChanged, moveVisibility);
        }

        var duplicateCommand = new DuplicateShortcutCommand(shortcut, onChanged);
        items.Add(WithShortcut(
            duplicateCommand,
            ctrl: true,
            alt: false,
            shift: true,
            VirtualKey.D,
            title: duplicateCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderDuplicate));

        AddPreSettingsCommands(items, createShortcutCommand, onChanged);
        items.Add(CreateSettingsItem(settings));

        // Delete
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

    /// <summary>
    /// Full home-list context menu (same as <see cref="Build"/>). Kept for call-site clarity.
    /// </summary>
    public static CommandContextItem[] BuildForHomePin(
        TerminalShortcut shortcut,
        Action onChanged,
        QuickShellSettingsManager settings,
        CreateShortcutCommand? createShortcutCommand = null,
        bool? needsRepair = null,
        PinnedMoveVisibility moveVisibility = default) =>
        needsRepair ?? ShortcutHealth.WouldNeedRepair(shortcut)
            ? BuildRepairOnly(shortcut, onChanged, settings)
            : Build(shortcut, onChanged, settings, createShortcutCommand, includeEdit: true, moveVisibility);

    public static CommandContextItem[] BuildRepairOnly(
        TerminalShortcut shortcut,
        Action onChanged,
        QuickShellSettingsManager? settings = null)
    {
        var items = new List<CommandContextItem>();

        if (settings is not null)
        {
            AddStatusCommand(items, shortcut, settings, onChanged);
            AddLaunchDiagnosticsCommand(items);
        }

        items.Add(WithShortcut(
            new ShortcutFormPage(shortcut, onChanged),
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.E,
            title: Strings.Menu_Edit,
            showInHoverActions: true,
            hoverOrder: HoverOrderEdit));

        if (shortcut.IsPinned)
        {
            var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned);
            items.Add(WithShortcut(
                favoriteCommand,
                ctrl: true,
                alt: false,
                shift: false,
                VirtualKey.F,
                title: favoriteCommand.Name,
                showInHoverActions: true,
                hoverOrder: HoverOrderFavorite));
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

    public static CommandContextItem[] BuildUndoRedoCommands(Action onChanged) =>
    [
        WithShortcut(
            new UndoShortcutCommand(onChanged),
            QuickShellKeyboardShortcuts.Undo,
            title: Strings.Menu_Undo,
            showInHoverActions: true,
            hoverOrder: HoverOrderUndo),
        WithShortcut(
            new RedoShortcutCommand(onChanged),
            QuickShellKeyboardShortcuts.Redo,
            title: Strings.Menu_Redo,
            showInHoverActions: true,
            hoverOrder: HoverOrderRedo),
    ];

    public static CommandContextItem[] BuildFormUndoRedoCommands(
        Func<bool> tryFormUndo,
        Func<bool> tryFormRedo,
        Action onRepositoryChanged) =>
    [
        WithShortcut(
            new WorkspaceFormUndoCommand(tryFormUndo, onRepositoryChanged),
            QuickShellKeyboardShortcuts.Undo,
            title: Strings.Menu_Undo,
            showInHoverActions: true,
            hoverOrder: HoverOrderUndo),
        WithShortcut(
            new WorkspaceFormRedoCommand(tryFormRedo, onRepositoryChanged),
            QuickShellKeyboardShortcuts.Redo,
            title: Strings.Menu_Redo,
            showInHoverActions: true,
            hoverOrder: HoverOrderRedo),
    ];

    private static void AddPreSettingsCommands(
        List<CommandContextItem> items,
        CreateShortcutCommand? createShortcutCommand,
        Action onChanged)
    {
        items.AddRange(BuildUndoRedoCommands(onChanged));

        if (createShortcutCommand is null)
        {
            return;
        }

        items.Add(new CommandContextItem(createShortcutCommand)
        {
            Title = Strings.Menu_CreateWorkspace,
            Icon = new IconInfo("\uE710"),
            RequestedShortcut = QuickShellKeyboardShortcuts.CreateShortcut,
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderCreate,
#endif
        });
    }

    private static void AddPinnedMoveCommands(
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        Action onChanged,
        PinnedMoveVisibility moveVisibility)
    {
        if (moveVisibility.ShowToTop)
        {
            var moveToTopCommand = new MoveFavoriteShortcutCommand(
                shortcut.Id, shortcut.Name, FavoriteMoveKind.ToTop, onChanged);
            items.Add(WithShortcut(
                moveToTopCommand,
                ctrl: true,
                alt: true,
                shift: true,
                VirtualKey.Home,
                title: moveToTopCommand.Name,
                hoverOrder: HoverOrderMoveToTop));
        }

        if (moveVisibility.ShowUp)
        {
            var moveUpCommand = new MoveFavoriteShortcutCommand(
                shortcut.Id, shortcut.Name, FavoriteMoveKind.Up, onChanged);
            items.Add(WithShortcut(
                moveUpCommand,
                ctrl: true,
                alt: true,
                shift: false,
                VirtualKey.Up,
                title: moveUpCommand.Name,
                showInHoverActions: true,
                hoverOrder: HoverOrderMoveUp));
        }

        if (moveVisibility.ShowDown)
        {
            var moveDownCommand = new MoveFavoriteShortcutCommand(
                shortcut.Id, shortcut.Name, FavoriteMoveKind.Down, onChanged);
            items.Add(WithShortcut(
                moveDownCommand,
                ctrl: true,
                alt: true,
                shift: false,
                VirtualKey.Down,
                title: moveDownCommand.Name,
                showInHoverActions: true,
                hoverOrder: HoverOrderMoveDown));
        }

        if (moveVisibility.ShowToBottom)
        {
            var moveToBottomCommand = new MoveFavoriteShortcutCommand(
                shortcut.Id, shortcut.Name, FavoriteMoveKind.ToBottom, onChanged);
            items.Add(WithShortcut(
                moveToBottomCommand,
                ctrl: true,
                alt: true,
                shift: true,
                VirtualKey.End,
                title: moveToBottomCommand.Name,
                hoverOrder: HoverOrderMoveToBottom));
        }
    }

    private static void AddFolderAndLinkCommands(List<CommandContextItem> items, TerminalShortcut shortcut)
    {
        items.Add(new CommandContextItem(new OpenShortcutFolderInExplorerCommand(shortcut.Id))
        {
            Title = Strings.Menu_OpenInFileExplorer,
            Icon = new IconInfo(""),
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderOpenExplorer,
#endif
        });

        items.Add(new CommandContextItem(new CopyShortcutPathCommand(shortcut.Id))
        {
            Title = Strings.Menu_CopyPath,
            Icon = new IconInfo(ShortcutGlyphs.CopyPath),
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderCopyPath,
#endif
        });

        if (!string.IsNullOrWhiteSpace(shortcut.DevServerUrl))
        {
            items.Add(new CommandContextItem(new OpenWorkspaceLinkCommand(shortcut.Id, WorkspaceLinkKind.DevServer))
            {
                Title = Strings.Menu_OpenDevServer,
                Icon = new IconInfo(""),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = HoverOrderDevServer,
#endif
            });
        }

        if (!string.IsNullOrWhiteSpace(shortcut.RepoUrl))
        {
            items.Add(new CommandContextItem(new OpenWorkspaceLinkCommand(shortcut.Id, WorkspaceLinkKind.Repo))
            {
                Title = Strings.Menu_OpenRepository,
                Icon = new IconInfo(ShortcutGlyphs.OpenRepository),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = HoverOrderRepo,
#endif
            });
        }

        if (CompanionAppLauncher.IsConfigured(shortcut))
        {
            var primaryPath = CompanionAppNormalization.GetPrimary(shortcut)?.Path ?? shortcut.CompanionAppPath;
            items.Add(new CommandContextItem(new OpenCompanionAppCommand(shortcut))
            {
                Title = Strings.Menu_OpenCompanionAppFormat(CompanionAppLauncher.BuildDisplaySummary(shortcut)),
                Icon = new IconInfo(CompanionAppCatalog.GetContextMenuIcon(primaryPath)),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = HoverOrderCompanionApp,
#endif
            });
        }
    }

    private static void AddStatusCommand(
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        QuickShellSettingsManager settings,
        Action onChanged)
    {
        items.Add(new CommandContextItem(new WorkspaceStatusPage(shortcut, settings, onChanged))
        {
            Title = "Workspace status…",
            Icon = new IconInfo(""),
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderStatus,
#endif
        });
    }

    private static void AddLaunchDiagnosticsCommand(List<CommandContextItem> items)
    {
        if (LaunchDiagnosticsState.LastReport is null)
        {
            return;
        }

        items.Add(new CommandContextItem(new CopyLaunchDiagnosticsCommand())
        {
            Title = "Copy launch diagnostics",
            Icon = new IconInfo(ShortcutGlyphs.CopyDiagnostics),
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderCopyDiagnostics,
#endif
        });
    }

    public static void AddElevationContextCommand(
        List<CommandContextItem> items,
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
            Title = Strings.Menu_RunAsAdmin,
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
            Title = Strings.Menu_RunNormally,
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
        WithShortcut(
            command,
            KeyChordHelpers.FromModifiers(ctrl, alt, shift, win: false, vkey: key),
            title,
            isCritical,
            showInHoverActions,
            hoverOrder);

    private static CommandContextItem WithShortcut(
        ICommand command,
        KeyChord shortcut,
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
            RequestedShortcut = shortcut,
        };
}
