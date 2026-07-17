using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;
using System.Threading;

namespace QuickShell.Pages;

internal partial class ShortcutFormPage : ContentPage, IDisposable
{
    private readonly IQuickShellServices _services;
    private readonly TerminalShortcut? _existing;
    private readonly TerminalShortcut? _createSeed;
    private readonly Action? _onSaved;
    private readonly bool _hasOnSaved;
    private readonly Lock _formSync = new();
    private WorkspaceEditor? _editor;
    private ShortcutForm? _form;
    private bool _formNeedsReset;
    private bool _commandsInitialized;
    private bool _disposed;

    public ShortcutFormPage(
        IQuickShellServices services,
        TerminalShortcut? existing = null,
        Action? onSaved = null,
        TerminalShortcut? createSeed = null)
    {
        _services = services;
        _existing = existing is null ? null : CloneShortcut(existing);
        _createSeed = existing is null ? createSeed : null;
        _onSaved = onSaved;
        _hasOnSaved = onSaved is not null;
        var isCreate = _existing is null;
        Id = isCreate
            ? $"com.quickshell.shortcut-form.create.{Guid.NewGuid():N}"
            : $"com.quickshell.shortcut-form.edit.{_existing!.Id}";
        Icon = new IconInfo("\uE70F");
        Title = isCreate ? "New workspace" : $"Edit {_existing!.Name}";
        Name = isCreate ? "Create" : "Edit";
    }

    public override IContent[] GetContent()
    {
        EnsureFormBuilt();
        return [_form!];
    }

    private void EnsureFormBuilt()
    {
        lock (_formSync)
        {
            if (_disposed)
            {
                return;
            }

            if (_form is null || _formNeedsReset)
            {
                _editor?.Dispose();
                _form?.Dispose();
                _editor = new WorkspaceEditor(_services, _services.Lifetime, _onSaved);
                _editor.ResetForOpen(_existing, _existing is null ? _createSeed : null);
                _form = new ShortcutForm(_editor, _services, MarkFormNeedsReset);
                _formNeedsReset = false;
            }
        }
    }

    private void MarkFormNeedsReset()
    {
        lock (_formSync)
        {
            _formNeedsReset = true;
        }
    }

    public void Dispose()
    {
        lock (_formSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _form?.Dispose();
            _editor?.Dispose();
            _form = null;
            _editor = null;
        }

        GC.SuppressFinalize(this);
    }

    private static TerminalShortcut CloneShortcut(TerminalShortcut shortcut) => new()
    {
        Id = shortcut.Id,
        Name = shortcut.Name,
        Abbreviation = shortcut.Abbreviation,
        Directory = shortcut.Directory,
        Command = shortcut.Command,
        Terminal = shortcut.Terminal,
        WtProfile = shortcut.WtProfile,
        RunAsAdmin = shortcut.RunAsAdmin,
        IsPinned = shortcut.IsPinned,
        PinOrder = shortcut.PinOrder,
        LastUsedUtc = shortcut.LastUsedUtc,
        Launches = [.. shortcut.Launches.Select(WorkspaceMapper.CloneEntry)],
        CompanionApps = [.. shortcut.CompanionApps.Select(CompanionAppNormalization.CloneEntry)],
        DevServerUrl = shortcut.DevServerUrl,
        RepoUrl = shortcut.RepoUrl,
        OpenCompanionAppOnLaunch = shortcut.OpenCompanionAppOnLaunch,
        OpenDevServerOnLaunch = shortcut.OpenDevServerOnLaunch,
        CompanionAppPath = shortcut.CompanionAppPath,
        CompanionAppArguments = shortcut.CompanionAppArguments,
    };
    private void EnsureCommandsInitialized()
    {
        if (!_hasOnSaved || _commandsInitialized || _onSaved is null)
        {
            return;
        }

        Commands = [.. ShortcutContextCommands.BuildFormUndoRedoCommands(
            () =>
            {
                EnsureFormBuilt();
                return _editor!.TryUndo();
            },
            () =>
            {
                EnsureFormBuilt();
                return _editor!.TryRedo();
            },
            _onSaved,
            _services)];
        _commandsInitialized = true;
    }

    private CommandResult HandleRefreshTerminals(string inputs)
    {
        MergeDraftFromInputs(inputs, out _);

        TerminalCatalog.InvalidateCache();
        ShortcutFormTemplateCache.Invalidate();

        var targets = TerminalCatalog.GetLaunchTargets(includeDefaultChoice: true);
        foreach (var command in _draft.Commands)
        {
            if (!targets.Any(t => t.Id.Equals(command.LaunchTarget, StringComparison.OrdinalIgnoreCase)))
            {
                command.LaunchTarget = "default";
            }
        }

        SyncDraftLaunchTargetFromCommands();
        SyncDraftRunAsAdminFromCommands();

        ApplyDraft(_draft, forceTemplateRebuild: true);
        return QuickShellNavigation.StayOpen(Strings.RefreshTerminals_Toast);
    }

    private CommandResult HandleBrowse(string inputs)
    {
        var initialDirectory = GetFieldFromPayload(inputs, "Directory") ?? _draft.Directory;
        MergeDraftFromInputs(inputs, out _, excludeDirectory: true);
        ClearSaveError();

        var selected = FolderPickerService.PickFolder(
            string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory);
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        ApplyDirectorySelection(selected);
        return CommandResult.KeepOpen();
    }

    private CommandResult HandlePaste(string inputs)
    {
        MergeDraftFromInputs(inputs, out _, excludeDirectory: true);
        ClearSaveError();

        if (!TryReadClipboardFolderPath(out var pasted, out var error))
        {
            return StayOnFormWithError(error);
        }

        ApplyDirectorySelection(pasted);
        return CommandResult.KeepOpen();
    }

    private void ApplyDirectorySelection(string directory)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out _))
        {
            normalized = directory.Trim();
        }

        _draft.Directory = normalized;

        if (ShouldAutofillNameFromDirectory())
        {
            _draft.Name = DeriveNameFromDirectory(normalized);
            _autoFilledName = _draft.Name;
        }

        if (string.IsNullOrWhiteSpace(_draft.RepoUrl))
        {
            _draft.RepoUrl = GitRepoDiscovery.TryGetRemoteUrl(normalized) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_draft.DevServerUrl))
        {
            _draft.DevServerUrl = _services.ProjectAnalysis.TryDetectDevServerUrl(normalized) ?? string.Empty;
        }

        // Commands and companions are not auto-seeded on Browse/Paste.
        // Discover create uses WorkspaceSeedFactory for heuristic launches + companion.
        ApplyDraft(_draft, forceTemplateRebuild: true);
        InvalidateSuggestionScan();
    }

    private bool ShouldAutofillNameFromDirectory()
    {
        if (string.IsNullOrWhiteSpace(_draft.Name))
        {
            _nameCustomized = false;
            return true;
        }

        if (_nameCustomized)
        {
            return false;
        }

        if (_autoFilledName is null)
        {
            return false;
        }

        return string.Equals(
            Normalize(_draft.Name),
            Normalize(_autoFilledName),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string DeriveNameFromDirectory(string directory)
    {
        var trimmed = directory.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
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

    private CommandResult HandleCancel(string payload)
    {
        if (_showingDiscardPrompt)
        {
            return LeaveShortcutForm(showToast: false);
        }

        if (!MergeDraftFromInputs(payload, out _))
        {
            return QuickShellNavigation.StayOpen(Strings.FormValues_ReadError);
        }

        if (!HasUnsavedChanges())
        {
            _services.Drafts.Clear();
            return LeaveShortcutForm(showToast: false);
        }

        PersistEditDraftIfNeeded();
        ShowDiscardPrompt();
        return CommandResult.KeepOpen();
    }

    private CommandResult HandleDiscardPromptAction(string inputs, string? data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);

        if (action == "discard")
        {
            _services.Drafts.Clear();
            return LeaveShortcutForm(showToast: false);
        }

        if (action == "save")
        {
            return SaveCurrentDraft();
        }

        return QuickShellNavigation.StayOpen(Strings.FormValues_ReadError);
    }

    private void ShowDiscardPrompt()
    {
        _showingDiscardPrompt = true;
        TemplateJson = ShortcutFormTemplateJson.BuildDiscardPromptTemplate();
        DataJson = "{}";
    }

    private CommandResult HandleSave(string payload)
    {
        if (!MergeDraftFromInputs(payload, out _))
        {
            return StayOnFormWithError(Strings.FormValues_ReadError);
        }

        return SaveCurrentDraft();
    }

    private CommandResult SaveCurrentDraft()
    {
        var draft = _draft;
        var originalName = string.IsNullOrWhiteSpace(draft.OriginalName) ? _originalName : draft.OriginalName;

        if (string.IsNullOrWhiteSpace(draft.Name) && !string.IsNullOrWhiteSpace(draft.Directory))
        {
            draft.Name = DeriveNameFromDirectory(draft.Directory);
            _autoFilledName = draft.Name;
        }

        // Local checks first so the banner names the problem before repository write.
        if (string.IsNullOrWhiteSpace(draft.Directory))
        {
            PersistEditDraftIfNeeded();
            return StayOnFormWithError("Folder path is required.");
        }

        if (!ShortcutValidation.DirectoryExists(draft.Directory.Trim()))
        {
            PersistEditDraftIfNeeded();
            return StayOnFormWithError($"Folder not found: {draft.Directory.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            PersistEditDraftIfNeeded();
            return StayOnFormWithError("Name is required.");
        }

        CompanionAppFormEditor.EnsureAtLeastOne(draft.Companions);
        for (var i = 0; i < draft.Companions.Count; i++)
        {
            var row = draft.Companions[i];
            if (!CompanionAppCatalog.TryValidateFormSelection(row.Preset, row.Path, out var companionSelectionError))
            {
                PersistEditDraftIfNeeded();
                return StayOnFormWithError(companionSelectionError);
            }

            if (!CompanionAppArgumentValidation.TryValidateForSave(
                    row.Preset,
                    row.Path,
                    row.Arguments,
                    out var companionArgumentError))
            {
                PersistEditDraftIfNeeded();
                return StayOnFormWithError(companionArgumentError);
            }

            row.Arguments = CompanionAppArgumentValidation.NormalizeForSave(row.Preset, row.Path, row.Arguments);
            ApplyCompanionFormState(
                i,
                CompanionAppCatalog.ReconcileForSave(row.Preset, row.Path, row.Arguments, row.OpenOnLaunch));
        }

        SyncCompanionLegacyScalars();

        var result = ShortcutFormSave.TrySave(
            originalName,
            draft.Name,
            draft.Abbreviation,
            draft.Directory,
            ShortcutFormLaunchSection.ToLaunchInputs(
                draft.Commands,
                draft.Name,
                draft.LaunchTarget),
            _services.Shortcuts,
            onSaved: null,
            draft.DevServerUrl,
            draft.RepoUrl,
            draft.OpenDevServerOnLaunch,
            draft.OpenCompanionAppOnLaunch,
            draft.CompanionAppPath,
            draft.CompanionAppArguments,
            CompanionAppFormEditor.ToCompanionEntries(draft.Companions));

        if (!result.Success)
        {
            PersistEditDraftIfNeeded();
            return StayOnFormWithError(result.Message);
        }

        ClearSaveError();
        _services.Drafts.Clear();
        // Mark home list stale only — never rebuild rows here. SubmitForm runs on a
        // host Task.Run; a full RefreshItems blocked GoBack for ~1–2s with ~45 workspaces.
        // QuickShellPage.Reload raises ItemsChanged (or relies on GetItems after nav).
        try
        {
            _onSaved?.Invoke();
        }
        catch
        {
            // Best-effort; repository write already succeeded.
        }

        // Valid save always leaves the form — never KeepOpen on success.
        return LeaveShortcutForm(
            string.IsNullOrWhiteSpace(result.Message) ? "Workspace saved." : result.Message);
    }

    private CommandResult StayOnFormWithError(string? message)
    {
        var error = string.IsNullOrWhiteSpace(message)
            ? "Could not save workspace. Fix the form and try again."
            : message.Trim();

        _saveError = error;
        // Refresh Adaptive Card data so the attention banner paints immediately.
        PublishDataJson(_draft);

        // Host-native toast that keeps the form open. Default ShowToast Result is Dismiss
        // (which would leave the page); KeepOpen must be explicit.
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = error,
            Result = CommandResult.KeepOpen(),
        });
    }

    private void ClearSaveError()
    {
        if (string.IsNullOrEmpty(_saveError))
        {
            return;
        }

        _saveError = string.Empty;
        PublishDataJson(_draft);
    }

    private CommandResult LeaveShortcutForm(string? toastMessage = null, bool showToast = true)
    {
        UnsubscribeFromDraftCleared();
        _saveError = string.Empty;
        // Navigate first; release form after so SubmitForm COM can finish cleanly.
        CommandResult result;
        if (showToast)
        {
            result = CommandResult.ShowToast(new ToastArgs
            {
                Message = string.IsNullOrWhiteSpace(toastMessage) ? "Workspace saved." : toastMessage,
                Result = CommandResult.GoBack(),
            });
        }
        else
        {
            result = CommandResult.GoBack();
        }

        try
        {
            // Soft release: keep form instance for fast re-open; next GetContent resets draft.
            _releaseForm?.Invoke();
        }
        catch
        {
            // Best-effort.
        }

        return result;
    }

    private void ApplyDraft(FormDraft draft, bool persist = true, bool forceTemplateRebuild = false)
    {
        CompanionAppFormEditor.EnsureAtLeastOne(draft.Companions);
        _draft = draft;
        var commandCount = Math.Max(1, draft.Commands.Count);
        var companionCount = Math.Max(1, draft.Companions.Count);
        if (forceTemplateRebuild
            || _templateCommandCount != commandCount
            || _templateCompanionCount != companionCount)
        {
            RebuildTemplate(draft.Commands, companionCount);
            _templateCommandCount = commandCount;
            _templateCompanionCount = companionCount;
        }

        PublishDataJson(draft);

        // Do NOT RaiseItemsChanged from inside SubmitForm. Host calls SubmitForm on
        // Task.Run over COM; nested ItemsChanged tears down the form proxy and surfaces
        // RPC_E_SERVER_UNAVAILABLE (0x800706BA). TemplateJson/DataJson PropChanged is enough.

        if (persist && _baselineReady)
        {
            PersistEditDraftIfNeeded();
        }
    }

    private void PublishDataJson(FormDraft draft)
    {
        // Single assignment only — intermediate "{}" PropChanged mid-submit can crash the host card.
        var scanSuggestions = !_suggestionScanComplete
            && !string.IsNullOrWhiteSpace(draft.Directory);
        DataJson = ShortcutFormTemplateJson.BuildDataJson(
            new ShortcutFormTemplateJson.DataPayload
            {
                OriginalName = draft.OriginalName,
                Name = draft.Name,
                Abbreviation = draft.Abbreviation,
                Directory = draft.Directory,
                LaunchTarget = draft.LaunchTarget,
                DevServerUrl = draft.DevServerUrl,
                RepoUrl = draft.RepoUrl,
                CompanionAppPreset = draft.CompanionAppPreset,
                CompanionAppPath = draft.CompanionAppPath,
                CompanionAppArguments = draft.CompanionAppArguments,
                Companions = draft.Companions,
                OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
                ShowRestoredDraftNote = _showRestoredDraftNote,
                ExpandSuggestionPills = draft.ExpandSuggestionPills,
                SuggestionScanning = scanSuggestions,
                SaveError = _saveError,
            },
            _services.ProjectAnalysis,
            _services.ClassificationCache,
            draft.Commands.Select(command => (command.Command, command.TaskType, command.LaunchTarget, command.RunAsAdmin)).ToList());
    }

    private void ScheduleSuggestionScan()
    {
        if (_suggestionScanComplete)
        {
            return;
        }

        var directory = _draft.Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            _suggestionScanComplete = true;
            return;
        }

        var generation = Interlocked.Increment(ref _suggestionScanGeneration);
        var usedCommands = _draft.Commands.Select(command => command.Command).ToArray();
        _ = Task.Run(() =>
        {
            try
            {
                _ = CommandSuggestionService.GetPills(
                    directory,
                    usedCommands,
                    _services.ProjectAnalysis,
                    _services.ClassificationCache);
            }
            catch
            {
                // Best effort — form remains usable without pills.
            }

            SettingsFormHelpers.ScheduleRefresh(
                () =>
                {
                    if (generation != _suggestionScanGeneration || _showingDiscardPrompt)
                    {
                        return;
                    }

                    _suggestionScanComplete = true;
                    try
                    {
                        PublishDataJson(_draft);
                    }
                    catch
                    {
                        // Form may have been released.
                    }
                },
                delayMs: 1);
        });
    }

    private void InvalidateSuggestionScan()
    {
        _suggestionScanComplete = false;
        Interlocked.Increment(ref _suggestionScanGeneration);
        ScheduleSuggestionScan();
    }

    private void PersistEditDraftIfNeeded()
    {
        if (_originalName is null || _showingDiscardPrompt)
        {
            return;
        }

        _services.Drafts.SaveIfDirty(
            _originalName,
            ToDraftData(_draft),
            ToDraftData(_baselineDraft),
            _nameCustomized,
            _autoFilledName);
    }

    private static ShortcutFormDraftData ToDraftData(FormDraft draft)
    {
        var first = draft.Commands.FirstOrDefault();
        return new ShortcutFormDraftData
        {
            OriginalName = draft.OriginalName,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Command = first?.Command ?? string.Empty,
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            Companions = draft.Companions.Select(row => new ShortcutFormCompanionDraftData
            {
                Id = row.Id,
                Preset = row.Preset,
                Path = row.Path,
                Arguments = row.Arguments,
                OpenOnLaunch = row.OpenOnLaunch,
            }).ToList(),
            RunAsAdmin = first?.RunAsAdmin ?? draft.RunAsAdmin,
            Launches = draft.Commands.Select(command => new ShortcutFormLaunchDraftData
            {
                Id = command.Id,
                Command = command.Command,
                LaunchTarget = command.LaunchTarget,
                RunAsAdmin = command.RunAsAdmin,
                IsEnabled = true,
                TaskType = command.TaskType,
            }).ToList(),
        };
    }

    private bool HasUnsavedChanges() => !DraftEquals(_draft, _baselineDraft);

    private bool MergeDraftFromInputs(string payload, out bool refreshForm, bool excludeDirectory = false)
    {
        refreshForm = false;
        var data = JsonNode.Parse(payload)?.AsObject();
        if (data is null)
        {
            return false;
        }

        if (data.Count == 0)
        {
            return true;
        }

        var mergedName = data["Name"]?.ToString() ?? _draft.Name;
        UpdateAutoFilledNameTracking(mergedName);

        var previousCompanions = _draft.Companions.Select(row => row.Clone()).ToList();
        var mergedCompanions = MergeCompanionsFromInputs(data, previousCompanions);

        _draft = new FormDraft
        {
            OriginalName = data["OriginalName"]?.ToString() ?? _draft.OriginalName,
            Name = mergedName,
            Abbreviation = data["Abbreviation"]?.ToString() ?? _draft.Abbreviation,
            Directory = excludeDirectory
                ? _draft.Directory
                : data["Directory"]?.ToString() ?? _draft.Directory,
            Commands = MergeCommandsFromInputs(data, _draft.Commands),
            LaunchTarget = data["LaunchTarget_0"]?.ToString()
                ?? data["LaunchTarget"]?.ToString()
                ?? _draft.LaunchTarget,
            DevServerUrl = data["DevServerUrl"]?.ToString() ?? _draft.DevServerUrl,
            RepoUrl = data["RepoUrl"]?.ToString() ?? _draft.RepoUrl,
            OpenDevServerOnLaunch = ParseToggleBool(
                data["OpenDevServerOnLaunch"]?.ToString(),
                _draft.OpenDevServerOnLaunch),
            Companions = mergedCompanions,
            OpenCompanionAppOnLaunch = _draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = _draft.CompanionAppPreset,
            CompanionAppPath = _draft.CompanionAppPath,
            CompanionAppArguments = _draft.CompanionAppArguments,
            RunAsAdmin = false,
        };

        SyncDraftRunAsAdminFromCommands();
        refreshForm = ApplyCompanionPresetChanges(previousCompanions, mergedCompanions);
        SyncCompanionLegacyScalars();

        return true;
    }

    private bool ApplyCompanionPresetChanges(
        List<CompanionAppFormRow> previous,
        List<CompanionAppFormRow> current)
    {
        var changed = false;
        var count = Math.Min(previous.Count, current.Count);
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(previous[i].Preset, current[i].Preset, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyCompanionFormState(i, CompanionAppCatalog.CreateStateFromPreset(current[i].Preset));
            changed = true;
        }

        return changed;
    }

    private static bool IsBrowseCompanionAppAction(string inputs, string? data, out int index)
    {
        index = 0;
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (!string.Equals(action, CompanionAppFormEditor.BrowseAction, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        TryReadCompanionIndex(data, inputs, out index);
        return true;
    }

    private static bool IsAddCompanionAppAction(string inputs, string? data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return string.Equals(action, CompanionAppFormEditor.AddAction, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoveCompanionAppAction(string inputs, string? data, out int index)
    {
        index = -1;
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (!string.Equals(action, CompanionAppFormEditor.RemoveAction, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryReadCompanionIndex(data, inputs, out index);
    }

    private static bool TryReadCompanionIndex(string? data, string inputs, out int index)
    {
        index = 0;
        foreach (var source in new[] { data, inputs })
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var node = JsonNode.Parse(source)?.AsObject();
            if (node?["companionIndex"] is not null
                && int.TryParse(node["companionIndex"]?.ToString(), out index))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyCompanionFormState(int index, CompanionAppCatalog.CompanionAppFormState state)
    {
        CompanionAppFormEditor.EnsureAtLeastOne(_draft.Companions);
        if (index < 0 || index >= _draft.Companions.Count)
        {
            index = 0;
        }

        var row = _draft.Companions[index];
        row.Preset = state.Preset;
        row.Path = state.Path;
        row.Arguments = state.Arguments;
        row.OpenOnLaunch = state.LaunchOnWorkspaceOpen;
        SyncCompanionLegacyScalars();
    }

    private void SyncCompanionLegacyScalars()
    {
        CompanionAppFormEditor.SyncLegacyScalars(
            _draft.Companions,
            out var openOnLaunch,
            out var path,
            out var arguments,
            out var preset);
        _draft.OpenCompanionAppOnLaunch = openOnLaunch;
        _draft.CompanionAppPath = path;
        _draft.CompanionAppArguments = arguments;
        _draft.CompanionAppPreset = preset;
    }

    private static List<CompanionAppFormRow> MergeCompanionsFromInputs(
        JsonObject data,
        List<CompanionAppFormRow> existing)
    {
        var formCount = 0;
        for (var probe = 0; probe < CompanionAppFormEditor.MaxCount; probe++)
        {
            if (!data.ContainsKey($"CompanionAppPreset_{probe}"))
            {
                break;
            }

            formCount = probe + 1;
        }

        var count = formCount > 0 ? formCount : Math.Max(1, existing.Count);
        var merged = new List<CompanionAppFormRow>();
        for (var i = 0; i < count; i++)
        {
            var prior = i < existing.Count ? existing[i] : CompanionAppFormRow.Empty();
            merged.Add(new CompanionAppFormRow
            {
                Id = prior.Id,
                Preset = data[$"CompanionAppPreset_{i}"]?.ToString() ?? prior.Preset,
                Path = prior.Path,
                Arguments = data[$"CompanionAppArguments_{i}"]?.ToString() ?? prior.Arguments,
                OpenOnLaunch = prior.OpenOnLaunch,
            });
        }

        CompanionAppFormEditor.EnsureAtLeastOne(merged);
        return merged;
    }

    private List<LaunchRowDraft> MergeCommandsFromInputs(
        JsonObject data,
        List<LaunchRowDraft> existing)
    {
        var count = existing.Count;
        for (var probe = 0; probe < 64; probe++)
        {
            if (!data.ContainsKey($"LaunchCommand_{probe}"))
            {
                count = probe;
                break;
            }
        }

        if (count == 0)
        {
            return existing.ToList();
        }

        var fallbackLaunchTarget = GetDefaultRowLaunchTarget();
        var merged = new List<LaunchRowDraft>();
        for (var i = 0; i < count; i++)
        {
            var prior = i < existing.Count ? existing[i] : new LaunchRowDraft();
            var mergedCommand = data[$"LaunchCommand_{i}"]?.ToString() ?? prior.Command;

            // Backspacing a row's command back to blank in the text field is "make this row
            // available again", not "mark it intentionally folder-only" -- that's what the
            // Open to Directory pill is for. Without this, a row that had real content and got
            // manually cleared kept IsEditorPlaceholder=false from its prior non-blank state,
            // so ApplyPill's FindFirstEmptyCommandIndex would skip it forever (indistinguishable
            // from a deliberately-blank row) until the dedicated Clear button was used instead.
            var becameBlankViaEdit = !string.IsNullOrWhiteSpace(prior.Command) && string.IsNullOrWhiteSpace(mergedCommand);

            merged.Add(new LaunchRowDraft
            {
                Id = prior.Id,
                Command = mergedCommand,
                TaskType = TaskTypeCatalog.Normalize(data[$"LaunchType_{i}"]?.ToString() ?? prior.TaskType),
                LaunchTarget = data[$"LaunchTarget_{i}"]?.ToString()
                    ?? prior.LaunchTarget
                    ?? fallbackLaunchTarget,
                RunAsAdmin = ParseToggleBool(data[$"LaunchRunAsAdmin_{i}"]?.ToString(), prior.RunAsAdmin),
                IsEditorPlaceholder = becameBlankViaEdit || prior.IsEditorPlaceholder,
            });
        }

        return merged;
    }

    private string GetDefaultRowLaunchTarget()
    {
        if (_draft.Commands.Count > 0 && !string.IsNullOrWhiteSpace(_draft.Commands[0].LaunchTarget))
        {
            return _draft.Commands[0].LaunchTarget;
        }

        return string.IsNullOrWhiteSpace(_draft.LaunchTarget) ? "default" : _draft.LaunchTarget;
    }

    private void SyncDraftLaunchTargetFromCommands()
    {
        if (_draft.Commands.Count > 0)
        {
            _draft.LaunchTarget = _draft.Commands[0].LaunchTarget;
        }
    }

    private void SyncDraftRunAsAdminFromCommands()
    {
        _draft.RunAsAdmin = _draft.Commands.Count > 0 && _draft.Commands[0].RunAsAdmin;
    }

    private void UpdateAutoFilledNameTracking(string mergedName)
    {
        if (string.IsNullOrWhiteSpace(mergedName))
        {
            _nameCustomized = false;
            _autoFilledName = null;
            return;
        }

        if (_autoFilledName is not null
            && !string.Equals(
                Normalize(mergedName),
                Normalize(_autoFilledName),
                StringComparison.OrdinalIgnoreCase))
        {
            _autoFilledName = null;
            _nameCustomized = true;
            return;
        }

        if (_autoFilledName is null
            && !string.IsNullOrWhiteSpace(mergedName)
            && !string.IsNullOrWhiteSpace(_draft.Directory))
        {
            var derived = DeriveNameFromDirectory(_draft.Directory);
            if (!string.Equals(
                    Normalize(mergedName),
                    Normalize(derived),
                    StringComparison.OrdinalIgnoreCase))
            {
                _nameCustomized = true;
            }
        }
    }

    private static string? GetFieldFromPayload(string payload, string field) =>
        JsonNode.Parse(payload)?.AsObject()?[field]?.ToString();

    private bool IsDiscardPromptAction(string inputs, string? data)
    {
        if (!_showingDiscardPrompt)
        {
            return false;
        }

        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return action is "save" or "discard";
    }

    private static bool IsBrowseAction(string inputs, string? data) =>
        TryGetAction(data) == "browse" || TryGetActionFromInputs(inputs) == "browse";

    private static bool IsHelpAction(string inputs, string? data) =>
        TryGetAction(data) == "help" || TryGetActionFromInputs(inputs) == "help";

    private static bool IsPasteAction(string inputs, string? data) =>
        TryGetAction(data) == "paste" || TryGetActionFromInputs(inputs) == "paste";

    private static bool IsRefreshTerminalsAction(string inputs, string? data) =>
        TryGetAction(data) == "refreshTerminals" || TryGetActionFromInputs(inputs) == "refreshTerminals";

    private static bool IsAddSuggestedCommandAction(
        string inputs,
        string? data,
        out string? pillCommand,
        out string? pillTaskType,
        out int pillIndex)
    {
        pillCommand = null;
        pillTaskType = null;
        pillIndex = -1;
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (!string.Equals(action, "addSuggestedCommand", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = data ?? inputs;
        var node = JsonNode.Parse(source)?.AsObject();
        if (node is null)
        {
            return false;
        }

        pillCommand = node["pillCommand"]?.ToString();
        pillTaskType = node["pillTaskType"]?.ToString();
        if (node["pillIndex"] is not null)
        {
            _ = int.TryParse(node["pillIndex"]?.ToString(), out pillIndex);
        }

        // action already matched "addSuggestedCommand" above -- that's sufficient signal on
        // its own. pillCommand is legitimately blank for the Open to Directory pill, and the
        // pill template never sends pillIndex at all, so requiring either non-blank pillCommand
        // or pillIndex >= 0 here made Open to Directory unrecognized and fall through to
        // default (save) handling.
        return true;
    }

    private static bool IsClearLaunchAction(string inputs, string? data, out int index)
    {
        index = -1;
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (!string.Equals(action, "clearLaunch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = data ?? inputs;
        var node = JsonNode.Parse(source)?.AsObject();
        return node?["launchIndex"] is not null
            && int.TryParse(node["launchIndex"]?.ToString(), out index);
    }

    private static bool IsExpandSuggestionPillsAction(string inputs, string? data) =>
        string.Equals(TryGetAction(data), "expandSuggestionPills", StringComparison.OrdinalIgnoreCase)
        || string.Equals(TryGetActionFromInputs(inputs), "expandSuggestionPills", StringComparison.OrdinalIgnoreCase);

    private static bool IsCollapseSuggestionPillsAction(string inputs, string? data) =>
        string.Equals(TryGetAction(data), "collapseSuggestionPills", StringComparison.OrdinalIgnoreCase)
        || string.Equals(TryGetActionFromInputs(inputs), "collapseSuggestionPills", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();

    private static bool IsCancelAction(string inputs, string? data)
    {
        if (TryGetAction(data) == "cancel")
        {
            return true;
        }

        return TryGetActionFromInputs(inputs) == "cancel";
    }

    private static string? TryGetAction(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        return JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();
    }

    private static FormDraft CloneDraft(FormDraft draft) =>
        new()
        {
            OriginalName = draft.OriginalName,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Commands = draft.Commands.Select(command => new LaunchRowDraft
            {
                Id = command.Id,
                Command = command.Command,
                TaskType = command.TaskType,
                LaunchTarget = command.LaunchTarget,
                RunAsAdmin = command.RunAsAdmin,
                IsEditorPlaceholder = command.IsEditorPlaceholder,
            }).ToList(),
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
            Companions = draft.Companions.Select(row => row.Clone()).ToList(),
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            RunAsAdmin = draft.RunAsAdmin,
            ExpandSuggestionPills = draft.ExpandSuggestionPills,
        };

    private static bool DraftEquals(FormDraft left, FormDraft right)
    {
        if (!string.Equals(Normalize(left.Name), Normalize(right.Name), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.Abbreviation), Normalize(right.Abbreviation), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.Directory), Normalize(right.Directory), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.LaunchTarget), Normalize(right.LaunchTarget), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.DevServerUrl), Normalize(right.DevServerUrl), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.RepoUrl), Normalize(right.RepoUrl), StringComparison.Ordinal)
            || left.OpenCompanionAppOnLaunch != right.OpenCompanionAppOnLaunch
            || !string.Equals(Normalize(left.CompanionAppPreset), Normalize(right.CompanionAppPreset), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.CompanionAppPath), Normalize(right.CompanionAppPath), StringComparison.Ordinal)
            || !string.Equals(Normalize(left.CompanionAppArguments), Normalize(right.CompanionAppArguments), StringComparison.Ordinal)
            || left.RunAsAdmin != right.RunAsAdmin
            || left.Companions.Count != right.Companions.Count)
        {
            return false;
        }

        if (left.Commands.Count != right.Commands.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Commands.Count; i++)
        {
            if (!string.Equals(Normalize(left.Commands[i].Command), Normalize(right.Commands[i].Command), StringComparison.Ordinal)
                || !string.Equals(TaskTypeCatalog.Normalize(left.Commands[i].TaskType), TaskTypeCatalog.Normalize(right.Commands[i].TaskType), StringComparison.Ordinal)
                || !string.Equals(Normalize(left.Commands[i].LaunchTarget), Normalize(right.Commands[i].LaunchTarget), StringComparison.Ordinal)
                || left.Commands[i].RunAsAdmin != right.Commands[i].RunAsAdmin)
            {
                return false;
            }
        }

        for (var i = 0; i < left.Companions.Count; i++)
        {
            if (!string.Equals(Normalize(left.Companions[i].Preset), Normalize(right.Companions[i].Preset), StringComparison.Ordinal)
                || !string.Equals(Normalize(left.Companions[i].Path), Normalize(right.Companions[i].Path), StringComparison.Ordinal)
                || !string.Equals(Normalize(left.Companions[i].Arguments), Normalize(right.Companions[i].Arguments), StringComparison.Ordinal)
                || left.Companions[i].OpenOnLaunch != right.Companions[i].OpenOnLaunch)
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private sealed class FormDraft
    {
        public string OriginalName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Abbreviation { get; set; } = string.Empty;

        public string Directory { get; set; } = string.Empty;

        public string DevServerUrl { get; set; } = string.Empty;

        public bool OpenDevServerOnLaunch { get; set; }

        public string RepoUrl { get; set; } = string.Empty;

        public List<CompanionAppFormRow> Companions { get; set; } = [CompanionAppFormRow.Empty()];

        public bool OpenCompanionAppOnLaunch { get; set; }

        public string CompanionAppPreset { get; set; } = CompanionAppCatalog.PresetNone;

        public string CompanionAppPath { get; set; } = string.Empty;

        public string CompanionAppArguments { get; set; } = string.Empty;

        public List<LaunchRowDraft> Commands { get; set; } = [new LaunchRowDraft()];

        public string LaunchTarget { get; set; } = "default";

        public bool RunAsAdmin { get; set; }

        public bool ExpandSuggestionPills { get; set; }
    }

    private sealed class FormEditSnapshot
    {
        public List<LaunchRowDraft> Commands { get; set; } = [];

        public List<CompanionAppFormRow> Companions { get; set; } = [];

        public bool ExpandSuggestionPills { get; set; }

        public FormEditSnapshot Clone() =>
            new()
            {
                Commands = LaunchRowListEditor.CloneRows(Commands),
                Companions = Companions.Select(row => row.Clone()).ToList(),
                ExpandSuggestionPills = ExpandSuggestionPills,
            };
    }

    private string FormTerminalChoicesJson() =>
        TerminalCatalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            _services.Settings?.TerminalApplicationId ?? TerminalHostIds.WindowsTerminal);

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
}
