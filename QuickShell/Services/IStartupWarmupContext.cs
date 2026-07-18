using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Services and state shared across startup warmup stages.
/// </summary>
internal interface IStartupWarmupContext
{
    IQuickShellServices Services { get; }

    QuickShellSettingsManager Settings { get; }

    IQuickShellLifetime Lifetime { get; }

    /// <summary>
    /// The workspace repository snapshot captured when the first real list was published.
    /// Stages should use this instead of re-querying the repository when possible.
    /// </summary>
    WorkspaceRepositorySnapshot? Snapshot { get; set; }
}
