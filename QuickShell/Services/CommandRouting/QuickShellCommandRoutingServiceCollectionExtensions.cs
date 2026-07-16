using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Commands;
using QuickShell.Composition;

namespace QuickShell.Services.CommandRouting;

/// <summary>
/// Registers CmdPal command routing handlers and <see cref="ICommandRouter"/>.
/// </summary>
internal static class QuickShellCommandRoutingServiceCollectionExtensions
{
    public static IServiceCollection AddQuickShellCommandRouting(
        this IServiceCollection services,
        QuickShellSettingsManager settingsManager,
        Action reloadPages)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(reloadPages);

        services.AddSingleton(settingsManager);
        services.AddSingleton<IQuickShellServices>(sp => new QuickShellServices(
            sp.GetRequiredService<IShortcutRepository>(),
            sp.GetRequiredService<IDraftStore>(),
            sp.GetRequiredService<QuickShellSettingsManager>(),
            sp.GetRequiredService<IProjectAnalysisService>()));
        services.AddSingleton(sp => new CreateShortcutCommand(reloadPages, sp.GetRequiredService<IQuickShellServices>()));
        services.AddSingleton(sp => new CommandItemFactoryContext
        {
            Services = sp.GetRequiredService<IQuickShellServices>(),
            Shortcuts = sp.GetRequiredService<IShortcutRepository>(),
            Settings = settingsManager,
            CreateShortcut = sp.GetRequiredService<CreateShortcutCommand>(),
            ReloadPages = reloadPages,
        });

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
        Action reloadPages,
        string? configDirectory = null)
    {
        services.AddQuickShellCore(configDirectory);
        services.AddQuickShellCommandRouting(settingsManager, reloadPages);
        return services;
    }
}
