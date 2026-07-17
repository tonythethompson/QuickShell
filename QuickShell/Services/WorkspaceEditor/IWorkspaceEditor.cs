using QuickShell.Models;

namespace QuickShell.Services.WorkspaceEditor;

internal interface IWorkspaceEditor : IDisposable
{
    WorkspaceEditState GetState();

    bool CanUndo { get; }

    bool CanRedo { get; }

    bool HasUnsavedChanges { get; }

    bool IsSuggestionScanning { get; }

    event EventHandler<WorkspaceEditChangedEventArgs>? Changed;

    void ResetForOpen(TerminalShortcut? existing, TerminalShortcut? createSeed);

    bool TryApplyInputs(string payload, bool excludeDirectory = false);

    WorkspaceEditResult SelectDirectory(string directory);

    WorkspaceEditResult TryAddSuggestedCommand(string? command, string? taskType, int pillIndex);

    WorkspaceEditResult ClearLaunchRow(int index);

    WorkspaceEditResult SetExpandSuggestionPills(bool expand);

    WorkspaceEditResult RefreshTerminals(IReadOnlyList<string> availableTargetIds, string defaultTargetId);

    WorkspaceEditResult AddCompanionRow();

    WorkspaceEditResult RemoveCompanionRow(int index);

    WorkspaceEditResult ApplyCompanionPreset(int index, string preset);

    WorkspaceEditResult SetCompanionExecutable(int index, string path);

    WorkspaceEditResult Save();

    WorkspaceEditResult Cancel();

    WorkspaceEditResult Discard();

    bool TryUndo();

    bool TryRedo();

    void LeaveForm();
}
