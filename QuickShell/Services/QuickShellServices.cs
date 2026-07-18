using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

internal sealed class QuickShellServices : IQuickShellServices
{
    public IShortcutRepository Shortcuts { get; }

    public IWorkspaceLaunchService WorkspaceLaunch { get; }

    public IDraftStore Drafts { get; }
    public QuickShellSettingsManager Settings { get; }
    public IProjectAnalysisService ProjectAnalysis { get; }
    public ICommandSuggestionService CommandSuggestions { get; }
    public IShortcutLaunchExecutor LaunchExecutor { get; }
    public IWorkspaceGitOperations GitOperations { get; }
    public ICompanionAppLauncher CompanionApps { get; }
    public IWorkspaceHealthChecker HealthChecker { get; }
    public WorkspaceGitLaunchGate GitLaunchGate { get; }
    public IGitRepoIndex GitRepos { get; }
    public IProjectClassificationCache ClassificationCache { get; }
    public IExtensionCallbackQueue CallbackQueue { get; }
    public IQuickShellLifetime Lifetime { get; }

    public QuickShellServices(
        IShortcutRepository shortcuts,
        IWorkspaceLaunchService workspaceLaunch,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IShortcutLaunchExecutor launchExecutor,
        IWorkspaceGitOperations gitOperations,
        ICompanionAppLauncher companionApps,
        IWorkspaceHealthChecker healthChecker,
        WorkspaceGitLaunchGate gitLaunchGate,
        IQuickShellLifetime lifetime,
        IGitRepoIndex gitRepos,
        IProjectClassificationCache classificationCache,
        IExtensionCallbackQueue callbackQueue)
    {
        Shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        WorkspaceLaunch = workspaceLaunch ?? throw new ArgumentNullException(nameof(workspaceLaunch));
        Drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ProjectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        CommandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
        LaunchExecutor = launchExecutor ?? throw new ArgumentNullException(nameof(launchExecutor));
        GitOperations = gitOperations ?? throw new ArgumentNullException(nameof(gitOperations));
        CompanionApps = companionApps ?? throw new ArgumentNullException(nameof(companionApps));
        HealthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        GitLaunchGate = gitLaunchGate ?? throw new ArgumentNullException(nameof(gitLaunchGate));
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        GitRepos = gitRepos ?? throw new ArgumentNullException(nameof(gitRepos));
        ClassificationCache = classificationCache ?? throw new ArgumentNullException(nameof(classificationCache));
        CallbackQueue = callbackQueue ?? throw new ArgumentNullException(nameof(callbackQueue));
        settings.InitializeServices(this);
        BeginShortcutPreload();
    }

    private void BeginShortcutPreload() => _ = PreloadShortcutsAsync();

    private async Task PreloadShortcutsAsync()
    {
        try { await Shortcuts.PreloadAsync(Lifetime.CancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or OperationCanceledException)
        {
            // Best-effort warm-up; synchronous access still loads on demand.
            Debug.WriteLine($"Shortcut preload skipped: {ex}");
        }
    }
}
