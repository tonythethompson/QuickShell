using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Suggestions;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

internal static class TestQuickShellServicesFactory
{
    public static QuickShellServices Create(IShortcutRepository repository, IDraftStore drafts, QuickShellSettingsManager settings, IProjectAnalysisService analysis, IQuickShellLifetime lifetime, LaunchTestBundle? launch = null)
    {
        var bundle = launch ?? LaunchTestServices.CreateBundle();
        var classificationCache = new ProjectClassificationCache(analysis);
        var commandSuggestions = new CommandSuggestionService(new ITaskSuggestionProvider[] { new WorkspaceSetupTaskSuggestionProvider(), new DockerComposeTaskSuggestionProvider(), new AgentCliSuggestionProvider() });
        var gitRepos = new GitRepoIndex(analysis, lifetime, new SyncExtensionThreadScheduler());
        var glyphs = new TerminalLaunchGlyphs(
            new TerminalProfileResolver(new QuickShellSettingsReader(appDataPaths: null, bundle.Catalog), bundle.Profiles, bundle.Catalog));
        var listIcons = new TerminalListIconCache(bundle.Profiles, glyphs, new AppDataPaths());
        var prewarm = new TerminalCatalogPrewarm(bundle.Catalog);
        return new QuickShellServices(
            repository,
            new WorkspaceLaunchService(repository, bundle.Executor, bundle.Companion),
            drafts,
            settings,
            analysis,
            commandSuggestions,
            bundle.Executor,
            bundle.Git,
            bundle.TargetStore,
            bundle.Companion,
            bundle.Health,
            bundle.GitGate,
            lifetime,
            gitRepos,
            classificationCache,
            new ExtensionCallbackQueue(),
            bundle.Catalog,
            bundle.Profiles,
            listIcons,
            glyphs,
            prewarm);
    }

    public static QuickShellServices Create(
        IShortcutRepository repository,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService analysis,
        IQuickShellLifetime lifetime,
        IGitRepoIndex gitRepos,
        LaunchTestBundle? launch = null)
    {
        var bundle = launch ?? LaunchTestServices.CreateBundle();
        var classificationCache = new ProjectClassificationCache(analysis);
        var commandSuggestions = new CommandSuggestionService(new ITaskSuggestionProvider[] { new WorkspaceSetupTaskSuggestionProvider(), new DockerComposeTaskSuggestionProvider(), new AgentCliSuggestionProvider() });
        var glyphs = new TerminalLaunchGlyphs(
            new TerminalProfileResolver(new QuickShellSettingsReader(appDataPaths: null, bundle.Catalog), bundle.Profiles, bundle.Catalog));
        var listIcons = new TerminalListIconCache(bundle.Profiles, glyphs, new AppDataPaths());
        var prewarm = new TerminalCatalogPrewarm(bundle.Catalog);
        return new QuickShellServices(
            repository,
            new WorkspaceLaunchService(repository, bundle.Executor, bundle.Companion),
            drafts,
            settings,
            analysis,
            commandSuggestions,
            bundle.Executor,
            bundle.Git,
            bundle.TargetStore,
            bundle.Companion,
            bundle.Health,
            bundle.GitGate,
            lifetime,
            gitRepos,
            classificationCache,
            new ExtensionCallbackQueue(),
            bundle.Catalog,
            bundle.Profiles,
            listIcons,
            glyphs,
            prewarm);
    }

    public static QuickShellServices CreateFromProvider(
        IServiceProvider provider,
        IShortcutRepository repository,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService analysis,
        IQuickShellLifetime lifetime,
        IGitRepoIndex? gitRepos = null) =>
        new(
            repository,
            new WorkspaceLaunchService(
                repository,
                provider.GetRequiredService<IShortcutLaunchExecutor>(),
                provider.GetRequiredService<ICompanionAppLauncher>()),
            drafts,
            settings,
            analysis,
            provider.GetRequiredService<ICommandSuggestionService>(),
            provider.GetRequiredService<IShortcutLaunchExecutor>(),
            provider.GetRequiredService<IWorkspaceGitOperations>(),
            provider.GetRequiredService<IWorktreeBranchTargetStore>(),
            provider.GetRequiredService<ICompanionAppLauncher>(),
            provider.GetRequiredService<IWorkspaceHealthChecker>(),
            provider.GetRequiredService<WorkspaceGitLaunchGate>(),
            lifetime,
            gitRepos ?? provider.GetRequiredService<IGitRepoIndex>(),
            provider.GetRequiredService<IProjectClassificationCache>(),
            new ExtensionCallbackQueue(),
            provider.GetRequiredService<ITerminalCatalog>(),
            provider.GetRequiredService<IWtProfilesService>(),
            provider.GetRequiredService<ITerminalListIconCache>(),
            provider.GetRequiredService<ITerminalLaunchGlyphs>(),
            provider.GetRequiredService<TerminalCatalogPrewarm>());
}
