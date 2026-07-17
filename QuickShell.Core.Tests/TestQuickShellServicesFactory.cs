using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Builds <see cref="QuickShellServices"/> for host-surface tests after the DI-shaped constructor.
/// </summary>
internal static class TestQuickShellServicesFactory
{
    public static QuickShellServices Create(
        IShortcutRepository repository,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService analysis,
        IQuickShellLifetime lifetime,
        LaunchTestBundle? launch = null)
    {
        var bundle = launch ?? LaunchTestServices.CreateBundle();
        var classificationCache = new ProjectClassificationCache(analysis);
        var gitRepos = new GitRepoIndex(analysis, lifetime, new SyncExtensionThreadScheduler());
        return new QuickShellServices(
            repository,
            drafts,
            settings,
            analysis,
            bundle.Executor,
            bundle.Git,
            bundle.Companion,
            bundle.Health,
            bundle.GitGate,
            lifetime,
            gitRepos,
            classificationCache,
            new ExtensionCallbackQueue());
    }

    public static QuickShellServices CreateFromProvider(
        IServiceProvider provider,
        IShortcutRepository repository,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService analysis,
        IQuickShellLifetime lifetime) =>
        new(
            repository,
            drafts,
            settings,
            analysis,
            provider.GetRequiredService<IShortcutLaunchExecutor>(),
            provider.GetRequiredService<IWorkspaceGitOperations>(),
            provider.GetRequiredService<ICompanionAppLauncher>(),
            provider.GetRequiredService<IWorkspaceHealthChecker>(),
            provider.GetRequiredService<WorkspaceGitLaunchGate>(),
            lifetime,
            provider.GetRequiredService<IGitRepoIndex>(),
            provider.GetRequiredService<IProjectClassificationCache>(),
            new ExtensionCallbackQueue());
}
