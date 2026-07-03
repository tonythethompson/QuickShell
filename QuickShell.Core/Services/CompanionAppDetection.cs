namespace QuickShell.Services;

internal sealed class CompanionAppSuggestion
{
    public required string PresetId { get; init; }

    public string? ExecutablePath { get; init; }

    public string Arguments { get; init; } = string.Empty;

    public bool EnableOnLaunch { get; init; }
}

internal static class CompanionAppDetection
{
    private static readonly string[] GitClientPresetPriority =
    [
        CompanionAppCatalog.PresetFork,
        CompanionAppCatalog.PresetGitHubDesktop,
    ];

    private static readonly string[] VisualStudioPresetPriority =
    [
        CompanionAppCatalog.PresetVs2026,
        CompanionAppCatalog.PresetVs2022,
    ];

    public static CompanionAppSuggestion? TrySuggestFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        return TrySuggestFromPreset(
                Directory.Exists(Path.Combine(directory, ".cursor")),
                CompanionAppCatalog.PresetCursor)
            ?? TrySuggestFromPreset(
                Directory.Exists(Path.Combine(directory, ".vscode")),
                CompanionAppCatalog.PresetVsCode)
            ?? TrySuggestFromPreset(
                Directory.Exists(Path.Combine(directory, ".obsidian")),
                CompanionAppCatalog.PresetObsidian)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasZedProject(directory),
                CompanionAppCatalog.PresetZed)
            ?? (WorkspaceCompanionSignals.HasJetBrainsProject(directory)
                && WorkspaceCompanionSignals.HasDotNetProject(directory)
                ? BuildSuggestion(CompanionAppCatalog.PresetRider)
                : null)
            ?? (WorkspaceCompanionSignals.HasVisualStudioSolution(directory)
                ? BuildFirstSuggestion(VisualStudioPresetPriority)
                : null)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasJetBrainsProject(directory),
                CompanionAppCatalog.PresetIntelliJIdea)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasSublimeProject(directory),
                CompanionAppCatalog.PresetSublime)
            ?? (WorkspaceCompanionSignals.HasGitRepository(directory)
                ? BuildFirstSuggestion(GitClientPresetPriority)
                : null);
    }

    private static CompanionAppSuggestion? TrySuggestFromPreset(bool condition, string presetId) =>
        condition ? BuildSuggestion(presetId) : null;

    private static CompanionAppSuggestion? BuildFirstSuggestion(IEnumerable<string> presetIds)
    {
        foreach (var presetId in presetIds)
        {
            var suggestion = BuildSuggestion(presetId);
            if (suggestion is not null)
            {
                return suggestion;
            }
        }

        return null;
    }

    private static CompanionAppSuggestion? BuildSuggestion(string presetId)
    {
        var executablePath = CompanionAppCatalog.TryResolveExecutable(presetId);
        if (executablePath is null)
        {
            return null;
        }

        return new CompanionAppSuggestion
        {
            PresetId = presetId,
            ExecutablePath = executablePath,
            Arguments = CompanionAppCatalog.GetDefaultArguments(presetId),
            EnableOnLaunch = true,
        };
    }
}
