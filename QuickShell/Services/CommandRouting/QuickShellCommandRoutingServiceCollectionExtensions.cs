using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Services.CommandRouting;

/// <summary>
/// Registers CmdPal command routing handlers and <see cref="ICommandRouter"/>.
/// </summary>
internal static class QuickShellCommandRoutingServiceCollectionExtensions
{
    public static IServiceCollection AddQuickShellCommandRouting(
        this IServiceCollection services,
        QuickShellSettingsManager settingsManager)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settingsManager);

        services.AddSingleton(settingsManager);
        services.AddSingleton<IQuickShellServices>(sp => new QuickShellServices(
            sp.GetRequiredService<IShortcutRepository>(),
            sp.GetRequiredService<IDraftStore>(),
            sp.GetRequiredService<QuickShellSettingsManager>(),
            sp.GetRequiredService<IProjectAnalysisService>(),
            sp.GetRequiredService<ICommandSuggestionService>(),
            sp.GetRequiredService<IShortcutLaunchExecutor>(),
            sp.GetRequiredService<IWorkspaceGitOperations>(),
            sp.GetRequiredService<ICompanionAppLauncher>(),
            sp.GetRequiredService<IWorkspaceHealthChecker>(),
            sp.GetRequiredService<WorkspaceGitLaunchGate>(),
            sp.GetRequiredService<IQuickShellLifetime>(),
            sp.GetRequiredService<IGitRepoIndex>(),
            sp.GetRequiredService<IProjectClassificationCache>(),
            sp.GetRequiredService<IExtensionCallbackQueue>()));
        services.AddSingleton(sp => new QuickShellHostServices(sp.GetRequiredService<IQuickShellServices>()));

        services.AddSingleton<ICommandItemHandler, OpenSettingsCommandHandler>();
        services.AddSingleton<ICommandItemHandler, ImportConflictCommandHandler>();
        services.AddSingleton<ICommandItemHandler, PendingShortcutEditCommandHandler>();
        services.AddSingleton<ICommandItemHandler, CreateWorkspaceCommandHandler>();
        services.AddSingleton<ICommandItemHandler, DiscoverCreateWorkspaceCommandHandler>();
        services.AddSingleton<ICommandItemHandler, DiscoverGitReposCommandHandler>();
        services.AddSingleton<ICommandItemHandler, OpenLaunchCommandHandler>();
        services.AddSingleton<ICommandItemHandler, OpenWorkspaceCommandHandler>();
        services.AddSingleton<ICommandItemHandler, WorkspaceStatusCommandHandler>();
        services.AddSingleton<ICommandItemHandler, WorktreeBranchPickerCommandHandler>();
        services.AddSingleton<ICommandItemHandler, WorktreeBranchSelectCommandHandler>();
        services.AddSingleton<ICommandItemHandler, WorktreeBranchClearCommandHandler>();
        services.AddSingleton<ICommandRouter, CommandRouter>();

        return services;
    }

    public static IServiceCollection AddQuickShellHost(
        this IServiceCollection services,
        QuickShellSettingsManager settingsManager,
        string? configDirectory = null,
        QuickShell.Abstractions.IQuickShellLifetime? lifetime = null)
    {
        // Capture extension SynchronizationContext at host registration (provider ctor thread).
        var extensionContext = SynchronizationContext.Current;

        services.AddQuickShellCore(configDirectory, lifetime);
        services.AddSingleton<IExtensionCallbackQueue, ExtensionCallbackQueue>();
        // Override Core's Sync scheduler with CmdPal-aware marshaling.
        services.AddSingleton<IExtensionThreadScheduler>(sp =>
            new CmdPalExtensionThreadScheduler(
                extensionContext,
                sp.GetRequiredService<IExtensionCallbackQueue>()));
        services.AddQuickShellCommandRouting(settingsManager);
        return services;
    }
}
