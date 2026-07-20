using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Classification.Classifiers;
using QuickShell.Classification.Detectors;
using QuickShell.Classification.Suggestions;
using QuickShell.Services;

namespace QuickShell.Composition;

/// <summary>
/// Composition root for <c>QuickShell.Core</c> services.
/// Prefer explicit factory registrations for AOT/trim analyzer friendliness.
/// </summary>
internal static class QuickShellServiceCollectionExtensions
{
    /// <summary>
    /// Registers core QuickShell services that the CmdPal host (and tests) resolve via DI.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configDirectory">
    /// Optional override for the shortcuts store directory (tests).
    /// When null, uses the default <c>%LOCALAPPDATA%\QuickShell</c> path.
    /// </param>
    /// <param name="lifetime">
    /// Optional shared process lifetime. When null, a default <see cref="QuickShellLifetime"/> is registered.
    /// </param>
    /// <param name="appDataRoot">
    /// Optional override for the app-data root services resolve via <see cref="IAppDataPaths"/>.
    /// When null, uses the real <c>%LOCALAPPDATA%</c> (tests inject a temp root instead of
    /// mutating the process-wide environment variable).
    /// </param>
    public static IServiceCollection AddQuickShellCore(
        this IServiceCollection services,
        string? configDirectory = null,
        IQuickShellLifetime? lifetime = null,
        string? appDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IQuickShellLifetime>(_ => lifetime ?? new QuickShellLifetime());
        services.AddSingleton<IAtomicFileWriter>(_ => new AtomicFileWriter());
        services.AddSingleton<IAppDataPaths>(_ => new AppDataPaths(appDataRoot));
        services.AddSingleton<IWtProfilesService>(_ => new WtProfilesService());
        services.AddSingleton<ITerminalCatalog>(sp =>
            new TerminalCatalog(sp.GetRequiredService<IWtProfilesService>()));
        services.AddSingleton<TerminalCatalogPrewarm>(sp =>
            new TerminalCatalogPrewarm(sp.GetRequiredService<ITerminalCatalog>()));
        services.AddSingleton<IShortcutRepository>(sp =>
            new ShortcutRepository(
                configDirectory,
                sp.GetRequiredService<IAtomicFileWriter>(),
                sp.GetRequiredService<IAppDataPaths>(),
                sp.GetRequiredService<ITerminalCatalog>()));
        services.AddSingleton<IWorktreeBranchTargetStore>(sp =>
            new WorktreeBranchTargetStore(
                sp.GetRequiredService<IAppDataPaths>(),
                sp.GetRequiredService<IAtomicFileWriter>()));
        services.AddSingleton<IWorkspaceLaunchService>(sp =>
            new WorkspaceLaunchService(
                sp.GetRequiredService<IShortcutRepository>(),
                sp.GetRequiredService<IShortcutLaunchExecutor>(),
                sp.GetRequiredService<ICompanionAppLauncher>()));
        services.AddSingleton<IDraftStore>(sp =>
            new ShortcutDraftStore(
                sp.GetRequiredService<IShortcutRepository>(),
                sp.GetRequiredService<IAtomicFileWriter>()));
        services.AddSingleton<ICommandIdParser>(_ => new CommandIdParser());

        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<QuickShellSettingsReader>(sp =>
            new QuickShellSettingsReader(
                sp.GetRequiredService<IAppDataPaths>(),
                sp.GetRequiredService<ITerminalCatalog>()));
        services.AddSingleton<ITerminalProfileResolver>(sp =>
            new TerminalProfileResolver(
                sp.GetRequiredService<QuickShellSettingsReader>(),
                sp.GetRequiredService<IWtProfilesService>(),
                sp.GetRequiredService<ITerminalCatalog>()));
        services.AddSingleton<ITerminalLaunchGlyphs>(sp =>
            new TerminalLaunchGlyphs(sp.GetRequiredService<ITerminalProfileResolver>()));
        services.AddSingleton<ITerminalListIconCache>(sp =>
            new TerminalListIconCache(
                sp.GetRequiredService<IWtProfilesService>(),
                sp.GetRequiredService<ITerminalLaunchGlyphs>(),
                sp.GetRequiredService<IAppDataPaths>()));
        services.AddSingleton<ITerminalLauncher>(sp =>
            new TerminalLauncher(
                sp.GetRequiredService<IProcessStarter>(),
                sp.GetRequiredService<ITerminalCatalog>()));
        services.AddSingleton<IWorkspaceEnvironmentProbe, WorkspaceEnvironmentProbe>();
        services.AddSingleton<IWorkspaceGitOperations, WorkspaceGitOperations>();
        services.AddSingleton<IWorkspaceHealthChecker>(sp =>
            new WorkspaceHealthCheck(
                sp.GetRequiredService<IWorkspaceEnvironmentProbe>(),
                sp.GetRequiredService<IWorkspaceGitOperations>(),
                sp.GetRequiredService<ITerminalCatalog>(),
                sp.GetRequiredService<IWtProfilesService>()));
        services.AddSingleton<WorkspaceGitLaunchGate>();
        services.AddSingleton<ICompanionAppLauncher, CompanionAppLauncher>();
        services.AddSingleton<IShortcutLaunchExecutor>(sp =>
            new ShortcutLaunchExecutor(
                sp.GetRequiredService<ITerminalLauncher>(),
                sp.GetRequiredService<IWorkspaceHealthChecker>(),
                sp.GetRequiredService<ICompanionAppLauncher>(),
                sp.GetRequiredService<WorkspaceGitLaunchGate>(),
                sp.GetRequiredService<IShortcutRepository>(),
                sp.GetRequiredService<ITerminalCatalog>()));
        services.AddSingleton<IWorkspaceMapper, WorkspaceMapper>();
        services.AddSingleton<IExtensionThreadScheduler, SyncExtensionThreadScheduler>();
        services.AddSingleton<IProjectClassificationCache, ProjectClassificationCache>();
        services.AddSingleton<IGitRepoIndex, GitRepoIndex>();
        services.AddSingleton<IRowPresentationDiagnostics, RowPresentationDiagnostics>();
        services.AddSingleton<IWorkspaceRowPresentationCache>(sp =>
            new WorkspaceRowPresentationCache(
                sp.GetRequiredService<IRowPresentationDiagnostics>(),
                sp.GetRequiredService<ITerminalCatalog>(),
                sp.GetRequiredService<ITerminalLaunchGlyphs>()));

        services.AddSingleton<IProjectLayoutAnalyzer, ProjectLayoutAnalyzer>();
        services.AddSingleton<IProjectClassifier, NodeProjectClassifier>();
        services.AddSingleton<IProjectClassifier, DotNetProjectClassifier>();
        services.AddSingleton<IProjectClassifier, DockerComposeProjectClassifier>();
        services.AddSingleton<IProjectClassifier, TaskRunnerProjectClassifier>();
        services.AddSingleton<IProjectClassifier, RustProjectClassifier>();
        services.AddSingleton<IProjectClassifier, PythonProjectClassifier>();
        services.AddSingleton<IProjectClassifier, EditorProjectClassifier>();
        services.AddSingleton<IProjectClassifier, GoProjectClassifier>();
        services.AddSingleton<IProjectClassifier, JavaProjectClassifier>();
        services.AddSingleton<IProjectClassifier, DenoProjectClassifier>();
        services.AddSingleton<IProjectClassifier, ProcfileProjectClassifier>();
        services.AddSingleton<IProjectClassifier, RubyProjectClassifier>();
        services.AddSingleton<IProjectClassifier, ElixirProjectClassifier>();
        services.AddSingleton<ICompanionAppDetector, CompanionAppDetector>();
        services.AddSingleton<IDevServerDetector, DevServerDetector>();
        services.AddSingleton<ITaskSuggestionProvider, WorkspaceSetupTaskSuggestionProvider>();
        services.AddSingleton<ITaskSuggestionProvider, DockerComposeTaskSuggestionProvider>();
        services.AddSingleton<ITaskSuggestionProvider, AgentCliSuggestionProvider>();
        services.AddSingleton<ICommandSuggestionService, CommandSuggestionService>();
        services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();

        services.AddSingleton<ICompanionAppArgumentValidation, CompanionAppArgumentValidationInstance>();
        services.AddSingleton<ICompanionAppNormalization, CompanionAppNormalizationInstance>();
        services.AddSingleton<IWorkspaceCompanionSignals, WorkspaceCompanionSignalsInstance>();
        services.AddSingleton<IInstallDiscovery, JetBrainsInstallDiscoveryInstance>();
        services.AddSingleton<IInstallDiscovery, VisualStudioInstallDiscoveryInstance>();

        return services;
    }
}
