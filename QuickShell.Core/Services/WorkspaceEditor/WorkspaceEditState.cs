namespace QuickShell.Services.WorkspaceEditor;

internal sealed record WorkspaceEditState(
    string? OriginalName,
    string Name,
    string Abbreviation,
    string Directory,
    string LaunchTarget,
    string DevServerUrl,
    string RepoUrl,
    bool OpenDevServerOnLaunch,
    bool OpenCompanionAppOnLaunch,
    string CompanionAppPreset,
    string CompanionAppPath,
    string CompanionAppArguments,
    IReadOnlyList<LaunchRowDraft> Commands,
    IReadOnlyList<CompanionAppFormRow> Companions,
    IReadOnlyList<CommandSuggestionPill> Pills,
    bool ExpandSuggestionPills,
    bool IsSuggestionScanning,
    bool ShowRestoredDraftNote,
    string? SaveError);
