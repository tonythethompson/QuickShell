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
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly QuickShellPageContext _context;
    private readonly QuickShellPage _page;
    private readonly ICommandRouter _commandRouter;
    private readonly Lazy<QuickShellFallbackPage> _fallbackPage;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly EventHandler _settingsChangedHandler;
    private readonly StartupWarmupCoordinator _warmupCoordinator;
    private volatile bool _disposed;

    public QuickShellCommandsProvider()
    {
        SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "start");

        using var startupTrace = StartupPerformanceTrace.Measure("CmdPal provider constructor");

        using (StartupPerformanceTrace.Measure("CmdPal settings manager"))
        {
            SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "before settings manager");
            // Settings + create/edit forms leave via SubmitForm — invalidate only (no list rebuild).
            _settingsManager = new QuickShellSettingsManager(InvalidatePagesAfterNavigation);
            SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "after settings manager");
        }

        using (StartupPerformanceTrace.Measure("CmdPal composition root"))
        {
            SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "before composition root");
            var collection = new ServiceCollection();
            var lifetime = new QuickShellLifetime();
            collection.AddQuickShellHost(_settingsManager, lifetime: lifetime);
            _services = collection.BuildServiceProvider();
            _lifetime = _services.GetRequiredService<IQuickShellLifetime>();

            var host = _services.GetRequiredService<QuickShellHostServices>();
            var createShortcut = new CreateShortcutCommand(ReloadPages, host.Services);
            _commandRouter = _services.GetRequiredService<ICommandRouter>();

            var warmupContext = new StartupWarmupContext(host.Services, _settingsManager, _lifetime);
            var warmupStages = StartupWarmupStages.Create(warmupContext);
            _warmupCoordinator = new StartupWarmupCoordinator(_lifetime, warmupContext, warmupStages);

            _context = new QuickShellPageContext(host, createShortcut, ReloadPages, _warmupCoordinator);
            SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "after composition root");
        }

        DisplayName = QuickShellBrand.DisplayName;
        Icon = QuickShellBrandIcons.App;
        Id = CommandDescriptor.ProviderId;
        Settings = _settingsManager.Settings;

        using (StartupPerformanceTrace.Measure("CmdPal page setup"))
        {
            SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "before page setup");
            _page = new QuickShellPage(_context);
            _settingsChangedHandler = (_, _) => _page.Reload();
            _settingsManager.SettingsChanged += _settingsChangedHandler;
            SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "after page setup");
        }

        var settingsPage = _settingsManager.SettingsPage;
        // Settings card, form catalogs, and Git discovery are warmed by the coordinator
        // after the first real workspace list is published.

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
                    new CommandContextItem(_context.CreateShortcut)
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
                    ..ShortcutContextCommands.BuildUndoRedoCommands(_context.Services, _context.ReloadRootPages),
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

        _fallbackPage = new Lazy<QuickShellFallbackPage>(() => new QuickShellFallbackPage(_context));
        _fallbacks = [new QuickShellFallback(_context, _fallbackPage)];

        SupportDiagnostics.Default.Write("QuickShellCommandsProvider.cs:ctor", "complete");
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    /// <summary>Immediate home-list rebuild (favorite moves, delete, undo, …).</summary>
    private void ReloadPages()
    {
        _services.GetRequiredService<IGitRepoIndex>().Invalidate();
        _page.Reload();
        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.ClearResults();
        }
    }

    /// <summary>Deferred list refresh after form/settings navigation (do not block GoBack).</summary>
    private void InvalidatePagesAfterNavigation()
    {
        _services.GetRequiredService<IGitRepoIndex>().Invalidate();
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

    public override ICommandItem? GetCommandItem(string id) =>
        _commandRouter.TryHandle(id, _context, out var item) ? item : base.GetCommandItem(id);

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _warmupCoordinator.Dispose();
        _settingsManager.SettingsChanged -= _settingsChangedHandler;
        _page.Dispose();
        if (_fallbackPage.IsValueCreated)
        {
            _fallbackPage.Value.Dispose();
        }

        foreach (var fallback in _fallbacks.OfType<IDisposable>())
        {
            fallback.Dispose();
        }

        _services.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
