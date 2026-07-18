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

    public static CommandContextItem CreateSettingsItem(IQuickShellServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new(services.Settings.SettingsPage)
        {
            Title = QuickShellBrand.SettingsTitle,
            Icon = new IconInfo("\ue713"),
        };
    }

    public static CommandContextItem[] Build(
        QuickShellPageContext context,
        TerminalShortcut shortcut,
        Action onChanged,
        bool includeEdit = true,
        PinnedMoveVisibility moveVisibility = default,
        Action? onFavoritesReordered = null,
        bool? includePageCommands = null,
        bool includePinnedMoveCommands = true,
        bool? needsRepair = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(onChanged);

        includePageCommands ??= false;

        // Context menus should expose the repair actions for missing workspace folders.
        // Callers building the home list precompute this with requireDirectoryExists=false
        // to avoid blocking first paint on WSL/network directory probes.
        if (needsRepair ?? ShortcutHealth.WouldNeedRepair(shortcut))
        {
            return BuildRepairOnly(context, shortcut, onChanged);
        }

        var items = new List<CommandContextItem>();

        // Open
        var enabledLaunches = ShortcutLaunchNormalization.GetLaunchesForDisplay(shortcut);
        if (enabledLaunches.Count > 1)
        {
            foreach (var launch in enabledLaunches)
            {
                items.Add(new CommandContextItem(new OpenShortcutLaunchCommand(shortcut, launch, context.Services))
                {
                    Title = ShortcutDisplay.GetLaunchContextMenuTitle(launch, enabledLaunches),
                    Icon = new IconInfo(TerminalLaunchGlyphs.GetForLaunch(launch)),
                });
            }
        }

        AddElevationContextCommand(context, items, shortcut);

        AddTrustContextCommand(items, shortcut, onChanged, context.Services);

        // Workspace
        AddFolderAndLinkCommands(context, items, shortcut);
        AddSwitchBranchCommand(context, items, shortcut, onChanged);

        // Status…
        AddStatusCommand(context, items, shortcut, onChanged);
        AddLaunchDiagnosticsCommand(items);

        // Manage
        if (includeEdit)
        {
            items.Add(WithShortcut(
                new ShortcutFormPage(context.Services, shortcut, onChanged),
                ctrl: true,
                alt: false,
                shift: false,
                VirtualKey.E,
                title: Strings.Menu_Edit,
                showInHoverActions: true,
                hoverOrder: HoverOrderEdit));
        }

        var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned, context.Services);
        items.Add(WithShortcut(
            favoriteCommand,
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.F,
            title: favoriteCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderFavorite));

        if (shortcut.IsPinned && includePinnedMoveCommands)
        {
            AddPinnedMoveCommands(context, items, shortcut, onFavoritesReordered ?? onChanged, moveVisibility);
        }

        var duplicateCommand = new DuplicateShortcutCommand(shortcut, onChanged, context.Services);
        items.Add(WithShortcut(
            duplicateCommand,
            ctrl: true,
            alt: false,
            shift: true,
            VirtualKey.D,
            title: duplicateCommand.Name,
            showInHoverActions: true,
            hoverOrder: HoverOrderDuplicate));

        if (includePageCommands == true)
        {
            AddPreSettingsCommands(context, items, onChanged);
        }
        else
        {
            items.Add(new CommandContextItem(context.CreateShortcut)
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

        items.Add(CreateSettingsItem(context.Services));

        // Delete
        var deleteCommand = new DeleteShortcutCommand(shortcut.Name, onChanged, context.Services);
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
    /// Home-list context menu without page-level history or favorites-reordering commands.
    /// </summary>
    public static CommandContextItem[] BuildForHomePin(
        QuickShellPageContext context,
        TerminalShortcut shortcut,
        Action onChanged,
        bool? needsRepair = null,
        PinnedMoveVisibility moveVisibility = default) =>
        (needsRepair ?? ShortcutHealth.WouldNeedRepair(shortcut, requireDirectoryExists: false))
            ? BuildRepairOnly(context, shortcut, onChanged)
            : Build(
                context,
                shortcut,
                onChanged,
                includeEdit: true,
                moveVisibility,
                includePageCommands: false,
                includePinnedMoveCommands: false,
                needsRepair: false);

    public static CommandContextItem[] BuildRepairOnly(
        QuickShellPageContext context,
        TerminalShortcut shortcut,
        Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(onChanged);

        var items = new List<CommandContextItem>();

        AddStatusCommand(context, items, shortcut, onChanged);
        AddLaunchDiagnosticsCommand(items);

        AddTrustContextCommand(items, shortcut, onChanged, context.Services);

        items.Add(WithShortcut(
            new ShortcutFormPage(context.Services, shortcut, onChanged),
            ctrl: true,
            alt: false,
            shift: false,
            VirtualKey.E,
            title: Strings.Menu_Edit,
            showInHoverActions: true,
            hoverOrder: HoverOrderEdit));

        if (shortcut.IsPinned)
        {
            var favoriteCommand = new ToggleFavoriteShortcutCommand(shortcut.Name, onChanged, shortcut.IsPinned, context.Services);
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

        var deleteCommand = new DeleteShortcutCommand(shortcut.Name, onChanged, context.Services);
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

    public static CommandContextItem[] BuildUndoRedoCommands(IQuickShellServices services, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(services);
        return
        [
            WithShortcut(
                new UndoShortcutCommand(onChanged, services),
                QuickShellKeyboardShortcuts.Undo,
                title: Strings.Menu_Undo,
                showInHoverActions: true,
                hoverOrder: HoverOrderUndo),
            WithShortcut(
                new RedoShortcutCommand(onChanged, services),
                QuickShellKeyboardShortcuts.Redo,
                title: Strings.Menu_Redo,
                showInHoverActions: true,
                hoverOrder: HoverOrderRedo),
        ];
    }

    public static CommandContextItem[] BuildFormUndoRedoCommands(
        Func<bool> tryFormUndo,
        Func<bool> tryFormRedo,
        Action onRepositoryChanged,
        IQuickShellServices services) =>
    [
        WithShortcut(
            new WorkspaceFormUndoCommand(tryFormUndo, onRepositoryChanged, services),
            QuickShellKeyboardShortcuts.Undo,
            title: Strings.Menu_Undo,
            showInHoverActions: true,
            hoverOrder: HoverOrderUndo),
        WithShortcut(
            new WorkspaceFormRedoCommand(tryFormRedo, onRepositoryChanged, services),
            QuickShellKeyboardShortcuts.Redo,
            title: Strings.Menu_Redo,
            showInHoverActions: true,
            hoverOrder: HoverOrderRedo),
    ];

    private static void AddPreSettingsCommands(
        QuickShellPageContext context,
        List<CommandContextItem> items,
        Action onChanged)
    {
        items.AddRange(BuildUndoRedoCommands(context.Services, onChanged));

        items.Add(new CommandContextItem(context.CreateShortcut)
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
        QuickShellPageContext context,
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        Action onChanged,
        PinnedMoveVisibility moveVisibility)
    {
        if (moveVisibility.ShowToTop)
        {
            var moveToTopCommand = new MoveFavoriteShortcutCommand(
                shortcut.Id, shortcut.Name, FavoriteMoveKind.ToTop, onChanged, context.Services);
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
                shortcut.Id, shortcut.Name, FavoriteMoveKind.Up, onChanged, context.Services);
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
                shortcut.Id, shortcut.Name, FavoriteMoveKind.Down, onChanged, context.Services);
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
                shortcut.Id, shortcut.Name, FavoriteMoveKind.ToBottom, onChanged, context.Services);
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

    private static void AddFolderAndLinkCommands(QuickShellPageContext context, List<CommandContextItem> items, TerminalShortcut shortcut)
    {
        items.Add(new CommandContextItem(new OpenShortcutFolderInExplorerCommand(shortcut.Id, context.Services))
        {
            Title = Strings.Menu_OpenInFileExplorer,
            Icon = new IconInfo("\ue838"),
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = true,
            HoverOrder = HoverOrderOpenExplorer,
#endif
        });

        items.Add(new CommandContextItem(new CopyShortcutPathCommand(shortcut.Id, context.Services))
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
            items.Add(new CommandContextItem(new OpenWorkspaceLinkCommand(shortcut.Id, WorkspaceLinkKind.DevServer, context.Services))
            {
                Title = Strings.Menu_OpenDevServer,
                Icon = new IconInfo("\ue774"),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = HoverOrderDevServer,
#endif
            });
        }

        if (!string.IsNullOrWhiteSpace(shortcut.RepoUrl))
        {
            items.Add(new CommandContextItem(new OpenWorkspaceLinkCommand(shortcut.Id, WorkspaceLinkKind.Repo, context.Services))
            {
                Title = Strings.Menu_OpenRepository,
                Icon = new IconInfo(ShortcutGlyphs.OpenRepository),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = HoverOrderRepo,
#endif
            });
        }

        if (context.Services.CompanionApps.IsConfigured(shortcut))
        {
            var primaryPath = CompanionAppNormalization.GetPrimary(shortcut)?.Path ?? shortcut.CompanionAppPath;
            items.Add(new CommandContextItem(new OpenCompanionAppCommand(shortcut, context.Services))
            {
                Title = Strings.Menu_OpenCompanionAppFormat(context.Services.CompanionApps.BuildDisplaySummary(shortcut)),
                Icon = new IconInfo(CompanionAppCatalog.GetContextMenuIcon(primaryPath)),
#if CMDPAL_HOVER_ACTIONS
                ShowInHoverActions = true,
                HoverOrder = HoverOrderCompanionApp,
#endif
            });
        }
    }

    private static void AddTrustContextCommand(
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        Action onChanged,
        IQuickShellServices services)
    {
        var stored = services.Shortcuts.GetStoredWorkspace(shortcut.Id);
        if (stored is null)
        {
            return;
        }

        if (stored.Security.IsTrusted)
        {
            items.Add(new CommandContextItem(new RevokeWorkspaceTrustCommand(shortcut.Id, onChanged, services))
            {
                Title = "Revoke workspace trust",
                Icon = new IconInfo("\uE72E"),
            });
        }
        else
        {
            items.Add(new CommandContextItem(new GrantWorkspaceTrustCommand(shortcut.Id, onChanged, services))
            {
                Title = "Trust workspace…",
                Icon = new IconInfo("\uE72E"),
            });
        }
    }

    /// <summary>
    /// Unconditional — deliberately does not check whether shortcut.Directory is actually a
    /// git repo here. That check needs git status, and this runs once per visible row on every
    /// home-list refresh; the "Workspace status…" page learned that lesson the hard way (see
    /// its constructor comment: eager git status made open take tens of seconds with ~45
    /// workspaces). WorktreeBranchPickerPage defers its own git work to GetItems(), so showing
    /// "Not a git repository" there instead of hiding this item entirely is the cheap tradeoff.
    /// </summary>
    private static void AddSwitchBranchCommand(
        QuickShellPageContext context,
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        Action onChanged)
    {
        items.Add(new CommandContextItem(new WorktreeBranchPickerPage(context.Services, shortcut.Id, onChanged))
        {
            Title = Strings.Menu_SwitchBranchEllipsis,
            Icon = new IconInfo(""),
        });
    }

    private static void AddStatusCommand(
        QuickShellPageContext context,
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        Action onChanged)
    {
        items.Add(new CommandContextItem(new WorkspaceStatusPage(context.Services, shortcut, onChanged))
        {
            Title = "Workspace status…",
            Icon = new IconInfo("\ue799"),
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
        QuickShellPageContext context,
        List<CommandContextItem> items,
        TerminalShortcut shortcut,
        bool insertAtStart = true)
    {
        CommandContextItem contextItem;
        if (shortcut.RunAsAdmin)
        {
            var standardCommand = new OpenTerminalShortcutCommand(shortcut, context.Services, runAsStandard: true);
            contextItem = CreateOpenWithoutAdminContextItem(standardCommand, showInHoverActions: true);
        }
        else
        {
            var adminCommand = new OpenTerminalShortcutCommand(shortcut, context.Services, runAsAdmin: true);
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
#if CMDPAL_HOVER_ACTIONS
            ShowInHoverActions = showInHoverActions,
            HoverOrder = hoverOrder,
#endif
            RequestedShortcut = shortcut,
            IsCritical = isCritical,
        };
}
