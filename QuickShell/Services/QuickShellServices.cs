using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Services.WorkspaceEditor;

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
    public IWorktreeBranchTargetStore TargetStore { get; }
    public ICompanionAppLauncher CompanionApps { get; }
    public IWorkspaceHealthChecker HealthChecker { get; }
    public WorkspaceGitLaunchGate GitLaunchGate { get; }
    public IGitRepoIndex GitRepos { get; }
    public IProjectClassificationCache ClassificationCache { get; }
    public IExtensionCallbackQueue CallbackQueue { get; }
    public IWorkspaceRowPresentationCache RowPresentation { get; }
    public IRowPresentationDiagnostics RowPresentationDiagnostics { get; }
    public ISettingsFormRefreshScheduler RefreshScheduler { get; }
    public IQuickShellLifetime Lifetime { get; }
    public ITerminalCatalog TerminalCatalog { get; }
    public IWtProfilesService WtProfiles { get; }
    public ITerminalListIconCache TerminalListIcons { get; }
    public ITerminalLaunchGlyphs TerminalLaunchGlyphs { get; }
    public TerminalCatalogPrewarm TerminalCatalogPrewarm { get; }
    public IShortcutFormViewBuilder FormViewBuilder { get; }
    public IWorkspaceEditorFactory WorkspaceEditors { get; }

    public QuickShellServices(
        IShortcutRepository shortcuts,
        IWorkspaceLaunchService workspaceLaunch,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IShortcutLaunchExecutor launchExecutor,
        IWorkspaceGitOperations gitOperations,
        IWorktreeBranchTargetStore targetStore,
        ICompanionAppLauncher companionApps,
        IWorkspaceHealthChecker healthChecker,
        WorkspaceGitLaunchGate gitLaunchGate,
        IQuickShellLifetime lifetime,
        IGitRepoIndex gitRepos,
        IProjectClassificationCache classificationCache,
        IExtensionCallbackQueue callbackQueue,
        ITerminalCatalog terminalCatalog,
        IWtProfilesService wtProfiles,
        ITerminalListIconCache terminalListIcons,
        ITerminalLaunchGlyphs terminalLaunchGlyphs,
        TerminalCatalogPrewarm terminalCatalogPrewarm,
        IShortcutFormViewBuilder formViewBuilder,
        IWorkspaceEditorFactory workspaceEditors,
        IWorkspaceRowPresentationCache? rowPresentation = null,
        IRowPresentationDiagnostics? rowPresentationDiagnostics = null,
        ISettingsFormRefreshScheduler? refreshScheduler = null)
    {
        Shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        WorkspaceLaunch = workspaceLaunch ?? throw new ArgumentNullException(nameof(workspaceLaunch));
        Drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ProjectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        CommandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
        LaunchExecutor = launchExecutor ?? throw new ArgumentNullException(nameof(launchExecutor));
        GitOperations = gitOperations ?? throw new ArgumentNullException(nameof(gitOperations));
        TargetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        CompanionApps = companionApps ?? throw new ArgumentNullException(nameof(companionApps));
        HealthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        GitLaunchGate = gitLaunchGate ?? throw new ArgumentNullException(nameof(gitLaunchGate));
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        GitRepos = gitRepos ?? throw new ArgumentNullException(nameof(gitRepos));
        ClassificationCache = classificationCache ?? throw new ArgumentNullException(nameof(classificationCache));
        CallbackQueue = callbackQueue ?? throw new ArgumentNullException(nameof(callbackQueue));
        TerminalCatalog = terminalCatalog ?? throw new ArgumentNullException(nameof(terminalCatalog));
        WtProfiles = wtProfiles ?? throw new ArgumentNullException(nameof(wtProfiles));
        TerminalListIcons = terminalListIcons ?? throw new ArgumentNullException(nameof(terminalListIcons));
        TerminalLaunchGlyphs = terminalLaunchGlyphs ?? throw new ArgumentNullException(nameof(terminalLaunchGlyphs));
        TerminalCatalogPrewarm = terminalCatalogPrewarm ?? throw new ArgumentNullException(nameof(terminalCatalogPrewarm));
        FormViewBuilder = formViewBuilder ?? throw new ArgumentNullException(nameof(formViewBuilder));
        WorkspaceEditors = workspaceEditors ?? throw new ArgumentNullException(nameof(workspaceEditors));
        RowPresentationDiagnostics = rowPresentationDiagnostics ?? new RowPresentationDiagnostics();
        RowPresentation = rowPresentation ?? new WorkspaceRowPresentationCache(
            RowPresentationDiagnostics,
            TerminalCatalog,
            TerminalLaunchGlyphs);
        RefreshScheduler = refreshScheduler ?? new SettingsFormRefreshScheduler(Lifetime, CallbackQueue);
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
