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
        return new QuickShellServices(repository, drafts, settings, analysis, commandSuggestions, bundle.Executor, bundle.Git, bundle.Companion, bundle.Health, bundle.GitGate, lifetime, gitRepos, classificationCache, new ExtensionCallbackQueue());
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
        return new QuickShellServices(repository, drafts, settings, analysis, commandSuggestions, bundle.Executor, bundle.Git, bundle.Companion, bundle.Health, bundle.GitGate, lifetime, gitRepos, classificationCache, new ExtensionCallbackQueue());
    }

    public static QuickShellServices CreateFromProvider(
        IServiceProvider provider,
        IShortcutRepository repository,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService analysis,
        IQuickShellLifetime lifetime,
        IGitRepoIndex? gitRepos = null) =>
        new(repository, drafts, settings, analysis, provider.GetRequiredService<ICommandSuggestionService>(), provider.GetRequiredService<IShortcutLaunchExecutor>(), provider.GetRequiredService<IWorkspaceGitOperations>(), provider.GetRequiredService<ICompanionAppLauncher>(), provider.GetRequiredService<IWorkspaceHealthChecker>(), provider.GetRequiredService<WorkspaceGitLaunchGate>(), lifetime, gitRepos ?? provider.GetRequiredService<IGitRepoIndex>(), provider.GetRequiredService<IProjectClassificationCache>(), new ExtensionCallbackQueue());
}
