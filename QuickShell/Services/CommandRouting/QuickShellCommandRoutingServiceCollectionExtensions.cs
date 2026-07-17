using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;

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
            sp.GetRequiredService<IShortcutLaunchExecutor>(),
            sp.GetRequiredService<IWorkspaceGitOperations>(),
            sp.GetRequiredService<ICompanionAppLauncher>(),
            sp.GetRequiredService<IWorkspaceHealthChecker>(),
            sp.GetRequiredService<WorkspaceGitLaunchGate>(),
            sp.GetRequiredService<IQuickShellLifetime>()));
        services.AddSingleton(sp => new QuickShellHostServices(sp.GetRequiredService<IQuickShellServices>()));
        services.AddSingleton<IWorkspaceEditorFactory, WorkspaceEditorFactory>();

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
        services.AddQuickShellCore(configDirectory, lifetime);
        services.AddQuickShellCommandRouting(settingsManager);
        return services;
    }
}
