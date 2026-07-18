using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Services;

/// <summary>
/// Built-in startup warmup stages for the CmdPal provider.
/// </summary>
internal static class StartupWarmupStages
{
    /// <summary>
    /// Creates the default stage list for a provider context:
    /// 1. terminal/profile catalogs,
    /// 2. settings content,
    /// 3. workspace form catalog (companion + template),
    /// 4. Git index for saved-workspace roots.
    /// </summary>
    public static IReadOnlyList<IStartupWarmupStage> Create(IStartupWarmupContext context)
    {
        return
        [
            new TerminalProfileCatalogWarmup(context.Settings),
            new SettingsContentWarmup(context.Settings),
            new WorkspaceFormCatalogWarmup(context.Settings),
            new GitIndexWarmup(context.Services.GitRepos, context.Services.Shortcuts),
        ];
    }

    private sealed class TerminalProfileCatalogWarmup : IStartupWarmupStage
    {
        private readonly QuickShellSettingsManager _settings;

        public TerminalProfileCatalogWarmup(QuickShellSettingsManager settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string Name => "terminal/profile catalogs";

        public void Execute(IStartupWarmupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings.PrewarmTerminalCatalog();
        }
    }

    private sealed class SettingsContentWarmup : IStartupWarmupStage
    {
        private readonly QuickShellSettingsManager _settings;

        public SettingsContentWarmup(QuickShellSettingsManager settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string Name => "settings content";

        public void Execute(IStartupWarmupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings.PrewarmSettingsContent();
        }
    }

    private sealed class WorkspaceFormCatalogWarmup : IStartupWarmupStage
    {
        private readonly QuickShellSettingsManager _settings;

        public WorkspaceFormCatalogWarmup(QuickShellSettingsManager settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string Name => "workspace form catalog";

        public void Execute(IStartupWarmupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShortcutFormCatalogPrewarm.Warm(_settings.TerminalApplicationId);
        }
    }

    private sealed class GitIndexWarmup : IStartupWarmupStage
    {
        private readonly IGitRepoIndex _gitRepoIndex;
        private readonly IShortcutRepository _shortcutRepository;

        public GitIndexWarmup(IGitRepoIndex gitRepoIndex, IShortcutRepository shortcutRepository)
        {
            _gitRepoIndex = gitRepoIndex ?? throw new ArgumentNullException(nameof(gitRepoIndex));
            _shortcutRepository = shortcutRepository ?? throw new ArgumentNullException(nameof(shortcutRepository));
        }

        public string Name => "Git index prewarm";

        public void Execute(IStartupWarmupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = context.Snapshot ?? _shortcutRepository.GetSnapshot();
            var roots = GitRepoSearchRoots.FromShortcuts(snapshot.Shortcuts).ToList();
            StartupPerformanceTrace.Write($"Git prewarm roots: {roots.Count}");

            if (roots.Count == 0)
            {
                StartupPerformanceTrace.Write("Git prewarm skipped: no saved-workspace roots");
                return;
            }

            _gitRepoIndex.Prewarm(roots, cancellationToken);
        }
    }
}
