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
    public static IServiceCollection AddQuickShellCore(
        this IServiceCollection services,
        string? configDirectory = null,
        IQuickShellLifetime? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IQuickShellLifetime>(_ => lifetime ?? new QuickShellLifetime());
        services.AddSingleton<IAtomicFileWriter>(_ => new AtomicFileWriter());
        services.AddSingleton<IShortcutRepository>(sp =>
            new ShortcutRepository(configDirectory, sp.GetRequiredService<IAtomicFileWriter>()));
        services.AddSingleton<IDraftStore>(sp =>
            new ShortcutDraftStore(
                sp.GetRequiredService<IShortcutRepository>(),
                sp.GetRequiredService<IAtomicFileWriter>()));
        services.AddSingleton<ICommandIdParser>(_ => new CommandIdParser());

        services.AddSingleton<ITerminalLauncher, TerminalLauncherService>();
        services.AddSingleton<ITerminalProfileResolver, TerminalProfileResolverService>();
        services.AddSingleton<IWorkspaceMapper, WorkspaceMapperService>();
        services.AddSingleton<IGitRepoIndex, GitRepoIndexService>();
        services.AddSingleton<IWorkspaceGitOperations, WorkspaceGitOperationsService>();
        services.AddTransient<IWorkspaceHealthChecker, WorkspaceHealthCheckerService>();

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
        services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();

        return services;
    }
}
