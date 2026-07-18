using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;

namespace QuickShell.Services;

/// <summary>
/// Host-facing service facade for the CmdPal extension. Constructor-injectable
/// singleton so pages and commands can be unit-tested in isolation.
/// </summary>
internal interface IQuickShellServices
{
    IShortcutRepository Shortcuts { get; }

    IWorkspaceLaunchService WorkspaceLaunch { get; }

    IDraftStore Drafts { get; }

    QuickShellSettingsManager Settings { get; }

    IProjectAnalysisService ProjectAnalysis { get; }

    ICommandSuggestionService CommandSuggestions { get; }

    IShortcutLaunchExecutor LaunchExecutor { get; }

    IWorkspaceGitOperations GitOperations { get; }

    ICompanionAppLauncher CompanionApps { get; }

    IWorkspaceHealthChecker HealthChecker { get; }

    WorkspaceGitLaunchGate GitLaunchGate { get; }

    IGitRepoIndex GitRepos { get; }

    IProjectClassificationCache ClassificationCache { get; }

    IExtensionCallbackQueue CallbackQueue { get; }

    IQuickShellLifetime Lifetime { get; }
}
