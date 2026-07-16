using QuickShell.Abstractions.Classification;
using QuickShell.Services;

namespace QuickShell.Classification.Detectors;

internal sealed class CompanionAppDetector : ICompanionAppDetector
{
    private static readonly string[] GitClientPresetPriority =
    [
        CompanionAppCatalog.PresetFork,
        CompanionAppCatalog.PresetGitKraken,
        CompanionAppCatalog.PresetSourcetree,
        CompanionAppCatalog.PresetGitHubDesktop,
    ];

    private static readonly string[] VisualStudioPresetPriority =
    [
        CompanionAppCatalog.PresetVs2026,
        CompanionAppCatalog.PresetVs2022,
    ];

    private static readonly string[] VsCodeFamilyPriority =
    [
        CompanionAppCatalog.PresetVsCode,
        CompanionAppCatalog.PresetVsCodeInsiders,
    ];

    public CompanionAppSuggestion? TrySuggest(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        // First installed match among ordered signals (not a ranked multi-choice UI).
        return TrySuggestFromPreset(
                Directory.Exists(Path.Combine(directory, ".cursor")),
                CompanionAppCatalog.PresetCursor)
            ?? (Directory.Exists(Path.Combine(directory, ".vscode"))
                ? BuildFirstSuggestion(VsCodeFamilyPriority)
                : null)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasKiroProject(directory),
                CompanionAppCatalog.PresetKiro)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasWindsurfProject(directory),
                CompanionAppCatalog.PresetDevin)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasAntigravityProject(directory),
                CompanionAppCatalog.PresetAntigravity)
            ?? TrySuggestFromPreset(
                Directory.Exists(Path.Combine(directory, ".obsidian")),
                CompanionAppCatalog.PresetObsidian)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasZedProject(directory),
                CompanionAppCatalog.PresetZed)
            ?? TrySuggestJetBrains(directory)
            ?? (WorkspaceCompanionSignals.HasVisualStudioSolution(directory)
                ? BuildFirstSuggestion(VisualStudioPresetPriority)
                : null)
            ?? TrySuggestFromPreset(
                WorkspaceCompanionSignals.HasSublimeProject(directory),
                CompanionAppCatalog.PresetSublime)
            ?? (WorkspaceCompanionSignals.HasGitRepository(directory)
                ? BuildFirstSuggestion(GitClientPresetPriority)
                : null);
    }

    private static CompanionAppSuggestion? TrySuggestJetBrains(string directory)
    {
        if (!WorkspaceCompanionSignals.HasJetBrainsProject(directory))
        {
            return null;
        }

        var candidates = new List<string>();

        if (WorkspaceCompanionSignals.HasGradleOrAndroidProject(directory))
        {
            candidates.Add(CompanionAppCatalog.PresetAndroidStudio);
        }

        if (WorkspaceCompanionSignals.HasDotNetProject(directory))
        {
            candidates.Add(CompanionAppCatalog.PresetRider);
        }

        if (WorkspaceCompanionSignals.HasGoMod(directory))
        {
            candidates.Add(CompanionAppCatalog.PresetGoLand);
        }

        if (WorkspaceCompanionSignals.HasPyprojectToml(directory))
        {
            candidates.Add(CompanionAppCatalog.PresetPyCharm);
        }

        if (WorkspaceCompanionSignals.HasPackageJson(directory))
        {
            candidates.Add(CompanionAppCatalog.PresetWebStorm);
        }

        if (WorkspaceCompanionSignals.HasCMakeProject(directory))
        {
            candidates.Add(CompanionAppCatalog.PresetCLion);
        }

        // Generic IntelliJ always remains a fallback for bare .idea projects.
        candidates.Add(CompanionAppCatalog.PresetIntelliJIdea);

        return BuildFirstSuggestion(CompanionAppPreference.PreferLastUsed(candidates));
    }

    private static CompanionAppSuggestion? TrySuggestFromPreset(bool condition, string presetId) =>
        condition ? BuildSuggestion(presetId) : null;

    private static CompanionAppSuggestion? BuildFirstSuggestion(IEnumerable<string> presetIds)
    {
        foreach (var presetId in CompanionAppPreference.PreferLastUsed(presetIds))
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
