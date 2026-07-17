using System.Text.Json;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

/// <summary>
/// CmdPal host facade seeded from the composition root at provider startup.
/// Pages and commands receive this instance through constructor injection.
/// </summary>
internal sealed class QuickShellServices : IQuickShellServices
{
    public IShortcutRepository Shortcuts { get; }

    public IDraftStore Drafts { get; }

    public QuickShellSettingsManager Settings { get; }

    public IProjectAnalysisService ProjectAnalysis { get; }

    public IShortcutLaunchExecutor LaunchExecutor { get; }

    public IWorkspaceGitOperations GitOperations { get; }

    public ICompanionAppLauncher CompanionApps { get; }

    public IWorkspaceHealthChecker HealthChecker { get; }

    public WorkspaceGitLaunchGate GitLaunchGate { get; }

    public IQuickShellLifetime Lifetime { get; }

    public QuickShellServices(
        IShortcutRepository shortcuts,
        IDraftStore drafts,
        QuickShellSettingsManager settings,
        IProjectAnalysisService projectAnalysis,
        IShortcutLaunchExecutor launchExecutor,
        IWorkspaceGitOperations gitOperations,
        ICompanionAppLauncher companionApps,
        IWorkspaceHealthChecker healthChecker,
        WorkspaceGitLaunchGate gitLaunchGate,
        IQuickShellLifetime lifetime)
    {
        Shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        Drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ProjectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        LaunchExecutor = launchExecutor ?? throw new ArgumentNullException(nameof(launchExecutor));
        GitOperations = gitOperations ?? throw new ArgumentNullException(nameof(gitOperations));
        CompanionApps = companionApps ?? throw new ArgumentNullException(nameof(companionApps));
        HealthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        GitLaunchGate = gitLaunchGate ?? throw new ArgumentNullException(nameof(gitLaunchGate));
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        settings.InitializeServices(this);
        BeginShortcutPreload();
    }

    private void BeginShortcutPreload() => _ = PreloadShortcutsAsync();

    private async Task PreloadShortcutsAsync()
    {
        try
        {
            await Shortcuts.PreloadAsync(Lifetime.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or OperationCanceledException)
        {
            // Best effort warm-up; synchronous access still loads on demand.
        }
    }
}
