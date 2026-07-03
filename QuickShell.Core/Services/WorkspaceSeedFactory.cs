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
        });

    public static TerminalShortcut ApplyDirectoryHints(TerminalShortcut seed)
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
            seed.DevServerUrl = DevServerUrlDetection.TryDetectDevServerUrl(seed.Directory);
        }

        if (!HasNonemptyLaunchCommand(seed))
        {
            var detected = DevServerUrlDetection.TryDetectDevLaunchCommand(seed.Directory);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                seed.Command = detected;
                ApplyDetectedCommandToLaunches(seed, detected);
            }
        }

        if (string.IsNullOrWhiteSpace(seed.CompanionAppPath))
        {
            var suggestion = CompanionAppDetection.TrySuggestFromDirectory(seed.Directory);
            if (suggestion is not null)
            {
                seed.CompanionAppPath = suggestion.ExecutablePath;
                seed.CompanionAppArguments = suggestion.Arguments;
                seed.OpenCompanionAppOnLaunch = suggestion.EnableOnLaunch;
            }
        }

        return seed;
    }

    private static bool HasNonemptyLaunchCommand(TerminalShortcut seed) =>
        seed.Launches.Any(launch => !string.IsNullOrWhiteSpace(launch.Command))
        || !string.IsNullOrWhiteSpace(seed.Command);

    private static void ApplyDetectedCommandToLaunches(TerminalShortcut seed, string command)
    {
        if (seed.Launches is { Count: > 0 })
        {
            var first = seed.Launches.OrderBy(launch => launch.Order).First();
            if (string.IsNullOrWhiteSpace(first.Command))
            {
                first.Command = command;
            }

            return;
        }

        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(seed);
    }
}
