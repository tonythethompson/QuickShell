using System;
using System.Collections.Generic;

namespace QuickShell.Services;

/// <summary>
/// Provider-scoped coordinator that stages optional startup warmups so they do not run
/// during provider construction. The first real workspace list signals the coordinator
/// to start work on a background thread.
/// </summary>
internal interface IStartupWarmupCoordinator : IDisposable
{
    /// <summary>
    /// Signals that the first real workspace list has been published. Idempotent.
    /// </summary>
    /// <param name="snapshot">The snapshot used to build the first list; may be null.</param>
    void SignalFirstListPublished(WorkspaceRepositorySnapshot? snapshot = null);

    /// <summary>True after <see cref="SignalFirstListPublished"/> has been called.</summary>
    bool IsStarted { get; }

    /// <summary>True after all stages have completed, failed, or been cancelled.</summary>
    bool IsCompleted { get; }

    /// <summary>Results for each stage that has finished.</summary>
    IReadOnlyList<StartupWarmupStageResult> StageResults { get; }
}
