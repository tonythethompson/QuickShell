using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Commands;
using QuickShell.Services.CommandRouting;
using QuickShell.Pages;
using QuickShell.Pages.Dev;
using QuickShell.Services;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell;

public partial class QuickShellCommandsProvider : CommandProvider, IDisposable
{
#if CMDPAL_HOVER_ACTIONS
    public override HoverActionsMode DefaultHoverActionsMode => HoverActionsMode.Explicit;
#endif
    private readonly ServiceProvider _services;
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly QuickShellPage _page;
    private readonly CreateShortcutCommand _createShortcutCommand;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly ICommandRouter _commandRouter;
    private readonly Lazy<QuickShellFallbackPage> _fallbackPage;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly EventHandler _settingsChangedHandler;

    public QuickShellCommandsProvider()
    {
        GitRepoIndex.ExtensionSynchronizationContext = SynchronizationContext.Current;
        using var startupTrace = StartupPerformanceTrace.Measure("CmdPal provider constructor");

        using (StartupPerformanceTrace.Measure("CmdPal settings manager"))
        {
            _settingsManager = new QuickShellSettingsManager(ReloadPages);
            _createShortcutCommand = new CreateShortcutCommand(ReloadPages);
        }

        using (StartupPerformanceTrace.Measure("CmdPal composition root"))
        {
            var collection = new ServiceCollection();
            collection.AddQuickShellHost(_settingsManager, _createShortcutCommand, ReloadPages);
            _services = collection.BuildServiceProvider();

            var shortcuts = (ShortcutRepository)_services.GetRequiredService<IShortcutRepository>();
            var drafts = (ShortcutDraftStore)_services.GetRequiredService<IDraftStore>();
            QuickShellServices.Bind(new QuickShellServices(shortcuts, drafts, _settingsManager));
            _commandRouter = _services.GetRequiredService<ICommandRouter>();
            KickoffGitRepoIndexPrewarm();
        }

        DisplayName = QuickShellBrand.DisplayName;
        Icon = QuickShellBrandIcons.App;
        Id = "com.quickshell";
        Settings = _settingsManager.Settings;

        using (StartupPerformanceTrace.Measure("CmdPal page setup"))
        {
            _discoverGitReposCommand = new OpenDiscoverGitReposCommand(ReloadPages);
            _page = new QuickShellPage(_settingsManager, _createShortcutCommand);
            _settingsChangedHandler = (_, _) => _page.Reload();
            _settingsManager.SettingsChanged += _settingsChangedHandler;
        }

        var settingsPage = _settingsManager.SettingsPage;
        SettingsFormHelpers.SchedulePostNavigationRefresh(_settingsManager.PrewarmSettingsContent);

        _commands =
        [
            new CommandItem(_page)
            {
                Title = DisplayName,
                Subtitle = "Open saved folders in any terminal you use",
                Icon = QuickShellBrandIcons.App,
#if CMDPAL_HOVER_ACTIONS
                HomeHoverActionsMode = HoverActionsMode.Explicit,
#endif
                MoreCommands =
                [
                    new CommandContextItem(_createShortcutCommand)
                    {
                        Title = "Create workspace",
                        Icon = new IconInfo("\uE710"),
                        RequestedShortcut = QuickShellKeyboardShortcuts.CreateShortcut,
#if CMDPAL_HOVER_ACTIONS
                        ShowInHoverActions = true,
                        HoverOrder = 0,
#endif
                    },
                    new CommandContextItem(settingsPage)
                    {
                        Title = QuickShellBrand.SettingsTitle,
                        Icon = new IconInfo("\uE713"),
#if CMDPAL_HOVER_ACTIONS
                        ShowInHoverActions = true,
                        HoverOrder = 10,
#endif
                    },
                    ..ShortcutContextCommands.BuildUndoRedoCommands(ReloadPages),
                    new CommandContextItem(new CmdPalFormReproIndexPage())
                    {
                        Title = "CmdPal form repros",
                        Icon = new IconInfo("\uE8FD"),
                    },
                ],
            },
        ];

        _fallbackPage = new Lazy<QuickShellFallbackPage>(() => new QuickShellFallbackPage(_settingsManager, ReloadPages));
        _fallbacks = [new QuickShellFallback(_fallbackPage, _discoverGitReposCommand, _settingsManager)];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    private void ReloadPages()
    {
        GitRepoIndex.Invalidate();
        _page.Reload();
        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.ClearResults();
        }
    }

    private static void KickoffGitRepoIndexPrewarm()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var shortcuts = QuickShellServices.Current.Shortcuts.GetShortcuts();
                GitRepoIndex.Prewarm(GitRepoSearchRoots.FromShortcuts(shortcuts));
            }
            catch
            {
                // Best effort; discover/create still work without the warm cache.
            }
        });
    }

    public override ICommandItem? GetCommandItem(string id)
    {
        if (_commandRouter.TryHandle(id, out var item))
        {
            return item;
        }

        return base.GetCommandItem(id);
    }

    public override void Dispose()
    {
        _settingsManager.SettingsChanged -= _settingsChangedHandler;
        _page.Dispose();
        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.Dispose();
        }

        QuickShellServices.Unbind();
        _services.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
