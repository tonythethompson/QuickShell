using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Suggestions;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Core.Tests;

internal static class TestQuickShellServicesFactory
{
    /// <summary>
    /// Creates a TerminalLaunchGlyphs instance with default test dependencies.
    /// Centralizes the repeated construction pattern used across ShortcutHealth and enrichment tests.
    /// </summary>
    public static TerminalLaunchGlyphs CreateGlyphs()
    {
        var testRoot = Path.Join(Path.GetTempPath(), "qs-test-appdata-" + Guid.NewGuid().ToString("N"));
        var appDataPaths = new AppDataPaths(testRoot);
        var profiles = new WtProfilesService();
        var catalog = new TerminalCatalog(profiles);
        var resolver = new TerminalProfileResolver(new QuickShellSettingsReader(appDataPaths, catalog), profiles, catalog);
        return new TerminalLaunchGlyphs(resolver);
    }

    /// <summary>
    /// Creates a TerminalCatalog instance with default test dependencies.
    /// Centralizes the repeated construction pattern used across ShortcutHealth tests.
    /// </summary>
    public static TerminalCatalog CreateCatalog()
    {
        var profiles = new WtProfilesService();
        return new TerminalCatalog(profiles);
    }

    /// <summary>
    /// Creates a fully configured <see cref="QuickShellServices"/> instance for testing.
    /// </summary>
    /// <param name="launch">Optional test service bundle to use when configuring the services.</param>
    /// <returns>The configured QuickShellServices instance.</returns>
    public static QuickShellServices Create(IShortcutRepository repository, IDraftStore drafts, QuickShellSettingsManager settings, IProjectAnalysisService analysis, IQuickShellLifetime lifetime, LaunchTestBundle? launch = null)
    {
        var bundle = launch ?? LaunchTestServices.CreateBundle();
        var classificationCache = new ProjectClassificationCache(analysis);
        var commandSuggestions = new CommandSuggestionService(new ITaskSuggestionProvider[] { new WorkspaceSetupTaskSuggestionProvider(), new DockerComposeTaskSuggestionProvider(), new AgentCliSuggestionProvider() });
        var gitRepos = new GitRepoIndex(analysis, lifetime, new SyncExtensionThreadScheduler());
        var (_, glyphs, listIcons, prewarm) = BuildTerminalWiring(bundle);
        var formViewBuilder = new ShortcutFormViewBuilder(bundle.Catalog, analysis, commandSuggestions);
        var editorFactory = new WorkspaceEditorFactory(repository, drafts, analysis, commandSuggestions, lifetime, "Add at least one launch.", "Open in terminal");
        var services = new QuickShellServices(
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
            prewarm,
            formViewBuilder,
            editorFactory);
        return services;
    }

    /// <summary>
    /// Creates a fully configured QuickShell services instance for tests.
    /// </summary>
    /// <param name="gitRepos">The Git repository index to use.</param>
    /// <param name="launch">Optional test service bundle supplying launch-related dependencies.</param>
    /// <returns>The configured QuickShell services instance.</returns>
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
        var (_, glyphs, listIcons, prewarm) = BuildTerminalWiring(bundle);
        var formViewBuilder = new ShortcutFormViewBuilder(bundle.Catalog, analysis, commandSuggestions);
        var editorFactory = new WorkspaceEditorFactory(repository, drafts, analysis, commandSuggestions, lifetime, "Add at least one launch.", "Open in terminal");
        var services = new QuickShellServices(
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
            prewarm,
            formViewBuilder,
            editorFactory);
        return services;
    }

    /// <summary>
    /// Creates a fully configured <see cref="QuickShellServices"/> instance using dependencies resolved from a service provider.
    /// </summary>
    /// <param name="provider">The service provider used to resolve required dependencies.</param>
    /// <param name="gitRepos">An optional Git repository index; the provider's index is used when omitted.</param>
    /// <returns>The configured QuickShell services.</returns>
    public static QuickShellServices CreateFromProvider(
        IServiceProvider provider,
        IShortcutRepository repository,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService analysis,
        IQuickShellLifetime lifetime,
        IGitRepoIndex? gitRepos = null)
    {
        var formViewBuilder = new ShortcutFormViewBuilder(
            provider.GetRequiredService<ITerminalCatalog>(),
            analysis,
            provider.GetRequiredService<ICommandSuggestionService>());
        var editorFactory = provider.GetRequiredService<IWorkspaceEditorFactory>();
        var services = new QuickShellServices(
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
            provider.GetRequiredService<TerminalCatalogPrewarm>(),
            formViewBuilder,
            editorFactory);
        return services;
    }

    /// <summary>
    /// Centralizes terminal-wiring construction (AppDataPaths, glyphs, icon cache, prewarm)
    /// used by both Create overloads. Returns a tuple with all four components initialized
    /// from a test-isolated temporary directory.
    /// </summary>
    private static (AppDataPaths appDataPaths, TerminalLaunchGlyphs glyphs, TerminalListIconCache listIcons, TerminalCatalogPrewarm prewarm) BuildTerminalWiring(LaunchTestBundle bundle)
    {
        var testRoot = Path.Join(Path.GetTempPath(), "qs-test-appdata-" + Guid.NewGuid().ToString("N"));
        var appDataPaths = new AppDataPaths(testRoot);
        var glyphs = new TerminalLaunchGlyphs(
            new TerminalProfileResolver(new QuickShellSettingsReader(appDataPaths, bundle.Catalog), bundle.Profiles, bundle.Catalog));
        var listIcons = new TerminalListIconCache(bundle.Profiles, glyphs, appDataPaths);
        var prewarm = new TerminalCatalogPrewarm(bundle.Catalog);
        return (appDataPaths, glyphs, listIcons, prewarm);
    }

}
