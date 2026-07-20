using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceClone
{
    public static TerminalShortcut Clone(TerminalShortcut source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Abbreviation = source.Abbreviation,
        Directory = source.Directory,
        Command = source.Command,
        Terminal = source.Terminal,
        WtProfile = source.WtProfile,
        RunAsAdmin = source.RunAsAdmin,
        IsPinned = source.IsPinned,
        PinOrder = source.PinOrder,
        LastUsedUtc = source.LastUsedUtc,
        Launches = (source.Launches ?? []).Select(WorkspaceMapper.CloneEntry).ToList(),
        CompanionApps = (source.CompanionApps ?? []).Select(CompanionAppNormalization.CloneEntry).ToList(),
        DevServerUrl = source.DevServerUrl,
        OpenDevServerOnLaunch = source.OpenDevServerOnLaunch,
        RepoUrl = source.RepoUrl,
        OpenCompanionAppOnLaunch = source.OpenCompanionAppOnLaunch,
        CompanionAppPath = source.CompanionAppPath,
        CompanionAppArguments = source.CompanionAppArguments,
    };
}
