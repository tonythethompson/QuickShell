using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Pages;

internal sealed partial class ShortcutForm : FormContent, IDisposable
{
    private readonly IWorkspaceEditor _editor;
    private readonly IQuickShellServices _services;
    private readonly Action? _onClosed;
    private readonly object _sync = new();
    private bool _disposed;
    private bool _showingDiscardPrompt;
    private int _templateCommandCount = -1;
    private int _templateCompanionCount = -1;

    public ShortcutForm(IWorkspaceEditor editor, IQuickShellServices services, Action? onClosed = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _onClosed = onClosed;
        _editor.Changed += OnEditorChanged;
        RebuildFromState(_editor.GetState());
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _editor.Changed -= OnEditorChanged;
        }
    }

    public override CommandResult SubmitForm(string inputs, string data) =>
        HandleSubmit(FormPayloadMerge.Merge(inputs, data), data);

    public override CommandResult SubmitForm(string payload) =>
        HandleSubmit(payload, null);

    private CommandResult HandleSubmit(string payload, string? data)
    {
        if (_showingDiscardPrompt)
        {
            return HandleDiscardPromptSubmit(payload, data);
        }

        var action = WorkspaceFormActionParser.Parse(payload, data);
        var excludeDirectory = action.Kind is WorkspaceFormActionKind.Browse or WorkspaceFormActionKind.Paste;

        if (!_editor.TryApplyInputs(payload, excludeDirectory))
        {
            return StayOnFormWithError(Strings.FormValues_ReadError);
        }

        return DispatchAction(action, payload);
    }

    private CommandResult HandleDiscardPromptSubmit(string payload, string? data)
    {
        var action = WorkspaceFormActionParser.ParseDiscardPromptAction(payload, data);
        switch (action.Kind)
        {
            case WorkspaceFormActionKind.Save:
                return MapResult(_editor.Save());
            case WorkspaceFormActionKind.Discard:
                return MapResult(_editor.Discard());
            default:
                return StayOnFormWithError(Strings.FormValues_ReadError);
        }
    }

    private CommandResult DispatchAction(WorkspaceFormAction action, string payload)
    {
        switch (action.Kind)
        {
            case WorkspaceFormActionKind.None:
            case WorkspaceFormActionKind.Help:
                return CommandResult.KeepOpen();
            case WorkspaceFormActionKind.Save:
                return MapResult(_editor.Save());
            case WorkspaceFormActionKind.Cancel:
                return HandleCancel();
            case WorkspaceFormActionKind.Discard:
                return MapResult(_editor.Discard());
            case WorkspaceFormActionKind.Browse:
                return HandleBrowse(payload);
            case WorkspaceFormActionKind.Paste:
                return HandlePaste();
            case WorkspaceFormActionKind.RefreshTerminals:
                return HandleRefreshTerminals();
            case WorkspaceFormActionKind.AddSuggestedCommand:
                return MapResult(_editor.TryAddSuggestedCommand(action.PillCommand, action.PillTaskType, action.PillIndex));
            case WorkspaceFormActionKind.ClearLaunch:
                return MapResult(_editor.ClearLaunchRow(action.LaunchIndex));
            case WorkspaceFormActionKind.ExpandSuggestionPills:
                return MapResult(_editor.SetExpandSuggestionPills(true));
            case WorkspaceFormActionKind.CollapseSuggestionPills:
                return MapResult(_editor.SetExpandSuggestionPills(false));
            case WorkspaceFormActionKind.AddCompanionApp:
                return MapResult(_editor.AddCompanionRow());
            case WorkspaceFormActionKind.RemoveCompanionApp:
                return MapResult(_editor.RemoveCompanionRow(action.CompanionIndex));
            case WorkspaceFormActionKind.BrowseCompanionApp:
                return HandleBrowseCompanionApp(action.CompanionIndex);
            case WorkspaceFormActionKind.ApplyCompanionPreset:
                return MapResult(_editor.ApplyCompanionPreset(action.CompanionIndex, action.Preset ?? string.Empty));
            default:
                return CommandResult.KeepOpen();
        }
    }

    private CommandResult HandleBrowse(string payload)
    {
        var initialDirectory = GetFieldFromPayload(payload, "Directory") ?? _editor.GetState().Directory;
        var selected = FolderPickerService.PickFolder(string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory);
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        return MapResult(_editor.SelectDirectory(selected));
    }

    private CommandResult HandlePaste()
    {
        if (!TryReadClipboardFolderPath(out var pasted, out var error))
        {
            return StayOnFormWithError(error);
        }

        return MapResult(_editor.SelectDirectory(pasted));
    }

    private CommandResult HandleBrowseCompanionApp(int index)
    {
        var selected = ShortcutFilePickerService.PickExecutableFile();
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        return MapResult(_editor.SetCompanionExecutable(index, selected));
    }

    private CommandResult HandleRefreshTerminals()
    {
        TerminalCatalog.InvalidateCache();
        ShortcutFormTemplateCache.Invalidate();
        var targets = TerminalCatalog.GetLaunchTargets(includeDefaultChoice: true);
        var targetIds = targets.Select(t => t.Id).ToList();
        return MapResult(_editor.RefreshTerminals(targetIds, "default"));
    }

    private CommandResult HandleCancel()
    {
        var result = _editor.Cancel();
        return MapResult(result);
    }

    private CommandResult MapResult(WorkspaceEditResult result)
    {
        var state = _editor.GetState();

        switch (result.Kind)
        {
            case WorkspaceEditResultKind.Saved:
                _editor.LeaveForm();
                _onClosed?.Invoke();
                return CommandResult.ShowToast(new ToastArgs
                {
                    Message = result.Message ?? "Workspace saved.",
                    Result = CommandResult.GoBack(),
                });
            case WorkspaceEditResultKind.Cancelled:
            case WorkspaceEditResultKind.Discarded:
                _editor.LeaveForm();
                _onClosed?.Invoke();
                return CommandResult.GoBack();
            case WorkspaceEditResultKind.PromptDiscard:
                _showingDiscardPrompt = true;
                TemplateJson = ShortcutFormTemplateJson.BuildDiscardPromptTemplate();
                DataJson = "{}";
                return CommandResult.KeepOpen();
            case WorkspaceEditResultKind.StayOpen:
            default:
                if (!string.IsNullOrWhiteSpace(state.SaveError))
                {
                    return CommandResult.ShowToast(new ToastArgs
                    {
                        Message = state.SaveError,
                        Result = CommandResult.KeepOpen(),
                    });
                }

                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    QuickShellStatus.ShowToast(result.Message);
                }

                return CommandResult.KeepOpen();
        }
    }

    private static CommandResult StayOnFormWithError(string? message)
    {
        var error = string.IsNullOrWhiteSpace(message)
            ? "Could not save workspace. Fix the form and try again."
            : message.Trim();

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = error,
            Result = CommandResult.KeepOpen(),
        });
    }

    private void OnEditorChanged(object? sender, WorkspaceEditChangedEventArgs e)
    {
        if (_disposed || _showingDiscardPrompt)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                RebuildFromState(e.State);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private void RebuildFromState(WorkspaceEditState state)
    {
        lock (_sync)
        {
            var commandCount = Math.Max(1, state.Commands.Count);
            var companionCount = Math.Max(1, state.Companions.Count);
            var terminalApplicationId = _services.Settings.TerminalApplicationId;
            var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();
            var taskTypeChoicesJson = TaskTypeCatalog.BuildFormChoicesJson(_services.ProjectAnalysis, state.Directory);

            if (_templateCommandCount != commandCount
                || _templateCompanionCount != companionCount)
            {
                TemplateJson = ShortcutFormTemplateCache.GetOrBuild(
                    commandCount,
                    terminalApplicationId,
                    companionChoicesJson,
                    taskTypeChoicesJson,
                    () => ShortcutFormTemplateJson.BuildTemplate(
                        FormTerminalChoicesJson(terminalApplicationId),
                        companionChoicesJson,
                        state.Commands.Select(c => (c.Command, c.TaskType, c.LaunchTarget, c.RunAsAdmin)).ToList(),
                        QuickShellBrand.DisplayName,
                        companionCount));
                _templateCommandCount = commandCount;
                _templateCompanionCount = companionCount;
            }

            DataJson = ShortcutFormTemplateJson.BuildDataJson(
                new ShortcutFormTemplateJson.DataPayload
                {
                    OriginalName = state.OriginalName ?? string.Empty,
                    Name = state.Name,
                    Abbreviation = state.Abbreviation,
                    Directory = state.Directory,
                    LaunchTarget = state.LaunchTarget,
                    DevServerUrl = state.DevServerUrl,
                    RepoUrl = state.RepoUrl,
                    CompanionAppPreset = state.CompanionAppPreset,
                    CompanionAppPath = state.CompanionAppPath,
                    CompanionAppArguments = state.CompanionAppArguments,
                    Companions = state.Companions,
                    OpenDevServerOnLaunch = state.OpenDevServerOnLaunch,
                    ShowRestoredDraftNote = state.ShowRestoredDraftNote,
                    ExpandSuggestionPills = state.ExpandSuggestionPills,
                    SuggestionScanning = state.IsSuggestionScanning,
                    SaveError = state.SaveError ?? string.Empty,
                },
                _services.ProjectAnalysis,
                state.Commands.Select(c => (c.Command, c.TaskType, c.LaunchTarget, c.RunAsAdmin)).ToList());
        }
    }

    private static string FormTerminalChoicesJson(string terminalApplicationId) =>
        TerminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true, terminalApplicationId);

    private static string? GetFieldFromPayload(string payload, string field)
    {
        if (JsonNode.Parse(payload) is not JsonObject obj)
        {
            return null;
        }

        return obj[field]?.ToString();
    }

    private static bool TryReadClipboardFolderPath(out string path, out string error)
    {
        path = string.Empty;
        error = string.Empty;

        var raw = StaClipboard.TryReadText()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = Strings.PasteClipboard_NoTextError;
            return false;
        }

        raw = UnwrapQuotedPath(raw);

        if (!ShortcutValidation.TryNormalizeDirectory(raw, out var normalized, out var validationError))
        {
            error = validationError;
            return false;
        }

        if (!ShortcutValidation.DirectoryExists(normalized))
        {
            error = Strings.DirectoryNotFound_ErrorFormat(normalized);
            return false;
        }

        path = normalized;
        return true;
    }

    private static string UnwrapQuotedPath(string value)
    {
        if (value.Length >= 2
            && ((value.StartsWith('"') && value.EndsWith('"'))
                || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1].Trim();
        }

        return value;
    }
}
