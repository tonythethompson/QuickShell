using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceSeedFactory
{
    public static TerminalShortcut FromGitRepo(GitRepoCandidate candidate, IProjectAnalysisService projectAnalysis) =>
        ApplyDirectoryHints(new TerminalShortcut
        {
            Name = candidate.Name,
            Directory = candidate.Directory,
            RepoUrl = candidate.RemoteUrl,
        }, candidate.Classification.Stacks == ProjectStack.None
            ? projectAnalysis.Classify(candidate.Directory)
            : candidate.Classification,
        projectAnalysis);

    public static TerminalShortcut FromGitRepoDirectory(string directory, IProjectAnalysisService projectAnalysis)
    {
        ArgumentNullException.ThrowIfNull(projectAnalysis);

        var trimmed = directory.Trim().TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = trimmed;
        }

        return FromGitRepo(new GitRepoCandidate
        {
            Directory = trimmed,
            Name = name,
            RemoteUrl = GitRepoDiscovery.TryGetRemoteUrl(trimmed),
            Classification = projectAnalysis.Classify(trimmed),
        }, projectAnalysis);
    }

    public static TerminalShortcut ApplyDirectoryHints(TerminalShortcut seed, IProjectAnalysisService projectAnalysis) =>
        ApplyDirectoryHints(seed, projectAnalysis.Classify(seed.Directory), projectAnalysis);

    private static TerminalShortcut ApplyDirectoryHints(TerminalShortcut seed, ProjectClassification classification, IProjectAnalysisService projectAnalysis)
    {
        ArgumentNullException.ThrowIfNull(projectAnalysis);

        if (string.IsNullOrWhiteSpace(seed.Directory))
        {
            return seed;
        }

        if (string.IsNullOrWhiteSpace(seed.RepoUrl))
        {
            seed.RepoUrl = GitRepoDiscovery.TryGetRemoteUrl(seed.Directory);
        }

        if (string.IsNullOrWhiteSpace(seed.DevServerUrl))
        {
            seed.DevServerUrl = projectAnalysis.TryDetectDevServerUrl(seed.Directory);
        }

        ApplyCompanionHints(seed, projectAnalysis);

        if (!HasNonemptyLaunchCommand(seed))
        {
            WorkspaceSetupSuggestion.ApplyToShortcut(seed, classification, projectAnalysis);
            ApplyInferredTaskTypes(seed, projectAnalysis);
        }

        return seed;
    }

    private static bool HasNonemptyLaunchCommand(TerminalShortcut seed) =>
        seed.Launches.Any(launch => !string.IsNullOrWhiteSpace(launch.Command))
        || !string.IsNullOrWhiteSpace(seed.Command);

    private static void ApplyInferredTaskTypes(TerminalShortcut seed, IProjectAnalysisService projectAnalysis)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(seed);
        var inferred = projectAnalysis.TryInferTaskType(seed.Directory);
        if (inferred is null)
        {
            return;
        }

        foreach (var launch in seed.Launches)
        {
            if (string.Equals(TaskTypeCatalog.Normalize(launch.TaskType), TaskTypeCatalog.None, StringComparison.Ordinal))
            {
                launch.TaskType = inferred;
            }
        }
    }

    private static void ApplyCompanionHints(TerminalShortcut seed, IProjectAnalysisService projectAnalysis)
    {
        if (!string.IsNullOrWhiteSpace(seed.CompanionAppPath))
        {
            return;
        }

        var suggestion = projectAnalysis.TrySuggestCompanionApp(seed.Directory);
        if (suggestion is null)
        {
            return;
        }

        CompanionAppNormalization.ApplyPrimaryFromScalars(
            seed,
            openOnLaunch: suggestion.EnableOnLaunch,
            path: suggestion.ExecutablePath,
            arguments: suggestion.Arguments);
    }
}
