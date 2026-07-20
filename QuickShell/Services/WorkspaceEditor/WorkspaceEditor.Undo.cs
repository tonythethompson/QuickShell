using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed partial class WorkspaceEditor
{
    /// <summary>
/// Saves the current editor state to the edit history before a change.
/// </summary>
private void PushEditSnapshot() => _editHistory.PushBeforeChange(CaptureEditSnapshot());

    /// <summary>
        /// Captures the current draft state for later restoration.
        /// </summary>
        /// <returns>A snapshot containing cloned command and companion data and the current editor settings.</returns>
        private FormEditSnapshot CaptureEditSnapshot() =>
        new()
        {
            Commands = LaunchRowListEditor.CloneRows(_draft.Commands),
            Companions = [.. _draft.Companions.Select(row => row.Clone())],
            ExpandSuggestionPills = _draft.ExpandSuggestionPills,
            AutoFilledName = _autoFilledName,
            NameCustomized = _nameCustomized,
        };

    /// <summary>
    /// Applies a saved edit snapshot to the current draft.
    /// </summary>
    /// <param name="restored">The edit snapshot to apply.</param>
    /// <returns><see langword="true"/> after the snapshot is applied.</returns>
    private bool ApplyEditSnapshot(FormEditSnapshot restored)
    {
        _draft.Commands = restored.Commands;
        _draft.Companions = [.. restored.Companions.Select(row => row.Clone())];
        CompanionAppFormEditor.EnsureAtLeastOne(_draft.Companions);
        SyncCompanionLegacyScalars();
        _draft.ExpandSuggestionPills = restored.ExpandSuggestionPills;
        _autoFilledName = restored.AutoFilledName;
        _nameCustomized = restored.NameCustomized;
        SyncDraftLaunchTargetFromCommands();
        SyncDraftRunAsAdminFromCommands();
        ApplyDraft();
        return true;
    }
}
