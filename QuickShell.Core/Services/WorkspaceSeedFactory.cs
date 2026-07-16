using QuickShell.Classification;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceSeedFactory
{
    public static TerminalShortcut FromGitRepo(GitRepoCandidate candidate) =>
        ApplyDirectoryHints(new TerminalShortcut
        {
            Name = candidate.Name,
            Directory = candidate.Directory,
            RepoUrl = candidate.RemoteUrl,
        }, candidate.Classification.Stacks == ProjectStack.None
            ? ProjectAnalysisAccessor.Instance.Classify(candidate.Directory)
            : candidate.Classification);

    public static TerminalShortcut FromGitRepoDirectory(string directory)
    {
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
            Classification = ProjectAnalysisAccessor.Instance.Classify(trimmed),
        });
    }

    public static TerminalShortcut ApplyDirectoryHints(TerminalShortcut seed) =>
        ApplyDirectoryHints(seed, ProjectAnalysisAccessor.Instance.Classify(seed.Directory));

    private static TerminalShortcut ApplyDirectoryHints(TerminalShortcut seed, ProjectClassification classification)
    {
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
            seed.DevServerUrl = ProjectAnalysisAccessor.Instance.TryDetectDevServerUrl(seed.Directory);
        }

        ApplyCompanionHints(seed);

        if (!HasNonemptyLaunchCommand(seed))
        {
            WorkspaceSetupSuggestion.ApplyToShortcut(seed, classification);
            ApplyInferredTaskTypes(seed);
        }

        return seed;
    }

    private static bool HasNonemptyLaunchCommand(TerminalShortcut seed) =>
        seed.Launches.Any(launch => !string.IsNullOrWhiteSpace(launch.Command))
        || !string.IsNullOrWhiteSpace(seed.Command);

    private static void ApplyInferredTaskTypes(TerminalShortcut seed)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(seed);
        var inferred = ProjectAnalysisAccessor.Instance.TryInferTaskType(seed.Directory);
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

    private static void ApplyCompanionHints(TerminalShortcut seed)
    {
        if (!string.IsNullOrWhiteSpace(seed.CompanionAppPath))
        {
            return;
        }

        var suggestion = ProjectAnalysisAccessor.Instance.TrySuggestCompanionApp(seed.Directory);
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
