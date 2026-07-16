using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Commands;
using QuickShell.Services.CommandRouting;
using QuickShell.Pages;
using QuickShell.Pages.Dev;
using QuickShell.Services;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell;

public sealed partial class QuickShellCommandsProvider : CommandProvider, IDisposable
{
#if CMDPAL_HOVER_ACTIONS
    public override HoverActionsMode DefaultHoverActionsMode => HoverActionsMode.Explicit;
#endif
    private readonly ServiceProvider _services;
    private readonly IQuickShellLifetime _lifetime;
    private readonly IQuickShellServices _quickShellServices;
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly QuickShellPage _page;
    private CreateShortcutCommand _createShortcutCommand;
    private readonly OpenDiscoverGitReposCommand _discoverGitReposCommand;
    private readonly ICommandRouter _commandRouter;
    private readonly Lazy<QuickShellFallbackPage> _fallbackPage;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly EventHandler _settingsChangedHandler;
    private volatile bool _disposed;

    public QuickShellCommandsProvider()
    {
        // #region agent log
        AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "start", hypothesisId: "B");
        // #endregion

        GitRepoIndex.ExtensionSynchronizationContext = SynchronizationContext.Current;
        GitRepoIndex.ExtensionThreadPoster = ExtensionCallbackQueue.Enqueue;
        using var startupTrace = StartupPerformanceTrace.Measure("CmdPal provider constructor");

        using (StartupPerformanceTrace.Measure("CmdPal settings manager"))
        {
            // #region agent log
            AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "before settings manager", hypothesisId: "A");
            // #endregion
            // Settings + create/edit forms leave via SubmitForm — invalidate only (no list rebuild).
            _settingsManager = new QuickShellSettingsManager(InvalidatePagesAfterNavigation);
            // #region agent log
            AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "after settings manager", hypothesisId: "A");
            // #endregion
        }

        using (StartupPerformanceTrace.Measure("CmdPal composition root"))
        {
            // #region agent log
            AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "before composition root", hypothesisId: "B");
            // #endregion
            var collection = new ServiceCollection();
            var lifetime = new QuickShellLifetime();
            collection.AddQuickShellHost(_settingsManager, ReloadPages, lifetime: lifetime);
            _services = collection.BuildServiceProvider();
            _lifetime = _services.GetRequiredService<IQuickShellLifetime>();

            var shortcuts = _services.GetRequiredService<IShortcutRepository>();
            var drafts = _services.GetRequiredService<IDraftStore>();
            var projectAnalysis = _services.GetRequiredService<IProjectAnalysisService>();
            ProjectAnalysisAccessor.Instance = projectAnalysis;
            _quickShellServices = _services.GetRequiredService<IQuickShellServices>();
            _settingsManager.Services = _quickShellServices;
            _createShortcutCommand = _services.GetRequiredService<CreateShortcutCommand>();
            _commandRouter = _services.GetRequiredService<ICommandRouter>();
            // #region agent log
            AgentDebugLog.Write(
                "QuickShellCommandsProvider.cs:ctor",
                "after composition root",
                new { shortcutCount = shortcuts.GetShortcuts().Count },
                hypothesisId: "B");
            // #endregion
            KickoffGitRepoIndexPrewarm();
            KickoffFormCatalogPrewarm();
        }

        DisplayName = QuickShellBrand.DisplayName;
        Icon = QuickShellBrandIcons.App;
        Id = "com.quickshell";
        Settings = _settingsManager.Settings;

        using (StartupPerformanceTrace.Measure("CmdPal page setup"))
        {
            // #region agent log
            AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "before page setup", hypothesisId: "D");
            // #endregion
            _discoverGitReposCommand = new OpenDiscoverGitReposCommand(ReloadPages, _quickShellServices);
            _page = new QuickShellPage(_settingsManager, _createShortcutCommand, _quickShellServices);
            _settingsChangedHandler = (_, _) => _page.Reload();
            _settingsManager.SettingsChanged += _settingsChangedHandler;
            // #region agent log
            AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "after page setup", hypothesisId: "D");
            // #endregion
        }

        var settingsPage = _settingsManager.SettingsPage;
        // Build settings Adaptive Card off the activation path so first open is warm.
        _ = Task.Run(() =>
        {
            try
            {
                _settingsManager.PrewarmSettingsContent();
            }
            catch
            {
                // Best effort.
            }
        });

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
                    ..ShortcutContextCommands.BuildUndoRedoCommands(ReloadPages, _quickShellServices),
#if DEBUG
                    new CommandContextItem(new CmdPalFormReproIndexPage())
                    {
                        Title = "CmdPal form repros",
                        Icon = new IconInfo("\uE8FD"),
                    },
#endif
                ],
            },
        ];

        _fallbackPage = new Lazy<QuickShellFallbackPage>(() => new QuickShellFallbackPage(_settingsManager, ReloadPages, _quickShellServices));
        _fallbacks = [new QuickShellFallback(_fallbackPage, _discoverGitReposCommand, _settingsManager, _quickShellServices)];

        // #region agent log
        AgentDebugLog.Write("QuickShellCommandsProvider.cs:ctor", "complete", hypothesisId: "B");
        // #endregion
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    /// <summary>Immediate home-list rebuild (favorite moves, delete, undo, …).</summary>
    private void ReloadPages()
    {
        GitRepoIndex.Invalidate();
        _page.Reload();
        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.ClearResults();
        }
    }

    /// <summary>Deferred list refresh after form/settings navigation (do not block GoBack).</summary>
    private void InvalidatePagesAfterNavigation()
    {
        GitRepoIndex.Invalidate();
        // _page may still be null while the provider ctor builds CreateShortcutCommand.
        if (_page is not null)
        {
            _page.InvalidateWorkspaces();
        }

        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.ClearResults();
        }
    }

    private void KickoffGitRepoIndexPrewarm()
    {
        if (_disposed || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                if (_disposed || _lifetime.IsCancellationRequested)
                {
                    return;
                }

                var shortcutRepository = _services.GetRequiredService<IShortcutRepository>();
                var shortcuts = shortcutRepository.GetShortcuts();
                var gitRepoIndex = _services.GetRequiredService<IGitRepoIndex>();
                gitRepoIndex.Prewarm(
                    GitRepoSearchRoots.FromShortcuts(shortcuts).ToList(),
                    _lifetime.CancellationToken);
            }
            catch
            {
                // Best effort; discover/create still work without the warm cache.
            }
        }, _lifetime.CancellationToken);
    }

    private void KickoffFormCatalogPrewarm()
    {
        if (_disposed || _lifetime.IsCancellationRequested)
        {
            return;
        }

        var terminalApplicationId = _settingsManager.TerminalApplicationId;
        _ = Task.Run(() =>
        {
            try
            {
                if (_disposed || _lifetime.IsCancellationRequested)
                {
                    return;
                }

                FormCatalogPrewarm.Warm(terminalApplicationId);
            }
            catch
            {
                // Best effort; first form open pays cold cost instead.
            }
        }, _lifetime.CancellationToken);
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _settingsManager.SettingsChanged -= _settingsChangedHandler;
        _page.Dispose();
        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.Dispose();
        }

        GitRepoIndex.ExtensionThreadPoster = null;
        _services.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
