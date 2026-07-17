namespace QuickShell.Services.WorkspaceEditor;

internal readonly record struct WorkspaceFormAction(
    WorkspaceFormActionKind Kind,
    string? PillCommand = null,
    string? PillTaskType = null,
    int PillIndex = -1,
    int LaunchIndex = -1,
    int CompanionIndex = -1,
    string? Preset = null);
