using QuickShell.Abstractions;

namespace QuickShell.Services;

/// <summary>
/// Concrete per-provider context passed to each warmup stage.
/// </summary>
internal sealed class StartupWarmupContext : IStartupWarmupContext
{
    public StartupWarmupContext(
        IQuickShellServices services,
        QuickShellSettingsManager settings,
        IQuickShellLifetime lifetime)
    {
        Services = services ?? throw new System.ArgumentNullException(nameof(services));
        Settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
        Lifetime = lifetime ?? throw new System.ArgumentNullException(nameof(lifetime));
    }

    public IQuickShellServices Services { get; }

    public QuickShellSettingsManager Settings { get; }

    public IQuickShellLifetime Lifetime { get; }

    public WorkspaceRepositorySnapshot? Snapshot { get; set; }
}
