using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal partial class ShortcutFormPage : ContentPage
{
    private readonly TerminalShortcut? _existing;
    private readonly TerminalShortcut? _createSeed;
    private readonly Action? _onSaved;
    private readonly object _formSync = new();

    public ShortcutFormPage(TerminalShortcut? existing = null, Action? onSaved = null, TerminalShortcut? createSeed = null)
    {
        _existing = existing is null ? null : CloneShortcut(existing);
        _createSeed = existing is null ? createSeed ?? ShortcutCreateNavigationState.TryTakeSeed() : null;
        _onSaved = onSaved;
        var isCreate = _existing is null;
        Id = isCreate
            ? $"com.quickshell.shortcut-form.create.{Guid.NewGuid():N}"
            : $"com.quickshell.shortcut-form.edit.{_existing!.Id}";
        Icon = new IconInfo("\uE70F");
        Title = isCreate ? "New workspace" : $"Edit {_existing!.Name}";
        Name = isCreate ? "Create" : "Edit";

        if (onSaved is not null)
        {
            Commands = ShortcutContextCommands.BuildFormUndoRedoCommands(
                () =>
                {
                    EnsureFormBuilt();
                    return _form!.TryUndoEdit();
                },
                () =>
                {
                    EnsureFormBuilt();
                    return _form!.TryRedoEdit();
                },
                onSaved);
        }
    }

    public override IContent[] GetContent()
    {
        EnsureFormBuilt();
        return [_form!];
    }

    private ShortcutForm? _form;

    private void EnsureFormBuilt()
    {
        lock (_formSync)
        {
            _form ??= new ShortcutForm(_existing, _createSeed, _onSaved, ReleaseForm);
        }
    }

    private void ReleaseForm()
    {
        lock (_formSync)
        {
            _form = null;
        }
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
        Launches = shortcut.Launches.Select(WorkspaceMapper.CloneEntry).ToList(),
        DevServerUrl = shortcut.DevServerUrl,
        RepoUrl = shortcut.RepoUrl,
        OpenCompanionAppOnLaunch = shortcut.OpenCompanionAppOnLaunch,
        OpenDevServerOnLaunch = shortcut.OpenDevServerOnLaunch,
        CompanionAppPath = shortcut.CompanionAppPath,
        CompanionAppArguments = shortcut.CompanionAppArguments,
    };
}

internal sealed partial class ShortcutForm : FormContent
{
    private readonly string? _originalName;
    private readonly Action? _onSaved;
    private readonly Action? _releaseForm;
    private FormDraft _draft = new();
    private FormDraft _baselineDraft = new();
    private string? _autoFilledName;
    private string? _autoFilledLaunchCommand;
    private bool _nameCustomized;
    private bool _showingDiscardPrompt;
    private bool _baselineReady;
    private bool _showRestoredDraftNote;
    private bool _subscribedToDraftCleared;
    private Action<string>? _draftClearedHandler;
    private readonly FormEditHistory<FormEditSnapshot> _editHistory =
        new(snapshot => snapshot.Clone());
    private int _templateCommandCount = -1;

    public ShortcutForm(TerminalShortcut? existing, TerminalShortcut? createSeed, Action? onSaved, Action? releaseForm = null)
    {
        _originalName = existing?.Name;
        _onSaved = onSaved;
        _releaseForm = releaseForm;

        var initial = existing ?? createSeed;
        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(initial ?? new TerminalShortcut());
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(initial, launchTarget);

        var companion = CompanionAppCatalog.ReconcileStoredShortcut(
            initial?.OpenCompanionAppOnLaunch ?? false,
            initial?.CompanionAppPath,
            initial?.CompanionAppArguments);

        ApplyDraft(new FormDraft
        {
            OriginalName = existing?.Name ?? string.Empty,
            Name = initial?.Name ?? string.Empty,
            Abbreviation = initial?.Abbreviation ?? string.Empty,
            Directory = initial?.Directory ?? string.Empty,
            DevServerUrl = initial?.DevServerUrl ?? string.Empty,
            RepoUrl = initial?.RepoUrl ?? string.Empty,
            OpenDevServerOnLaunch = initial?.OpenDevServerOnLaunch ?? false,
            OpenCompanionAppOnLaunch = companion.LaunchOnWorkspaceOpen,
            CompanionAppPreset = companion.Preset,
            CompanionAppPath = companion.Path,
            CompanionAppArguments = companion.Arguments,
            Commands = commands,
            LaunchTarget = launchTarget,
            RunAsAdmin = initial?.RunAsAdmin ?? false,
        }, persist: false);
        _baselineDraft = CloneDraft(_draft);
        _baselineReady = true;
        TryRestoreEditDraft();

        if (_originalName is not null)
        {
            // Subscribe via a weak-reference trampoline: the static Drafts.Cleared event
            // outlives this form, and the only guaranteed unsubscribe path is explicit
            // Save/Cancel. If the form is abandoned any other way (Escape, navigating
            // away), a direct `+= OnDraftStoreCleared` would root this instance forever.
            var weakSelf = new WeakReference<ShortcutForm>(this);
            Action<string>? handler = null;
            handler = originalName =>
            {
                if (weakSelf.TryGetTarget(out var self))
                {
                    self.OnDraftStoreCleared(originalName);
                }
                else
                {
                    QuickShellServices.Current.Drafts.Cleared -= handler;
                }
            };
            _draftClearedHandler = handler;
            QuickShellServices.Current.Drafts.Cleared += handler;
            _subscribedToDraftCleared = true;
        }
    }

    private void OnDraftStoreCleared(string originalName)
    {
        if (_originalName is null
            || !string.Equals(originalName, _originalName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ResetToSavedBaseline();
    }

    private void ResetToSavedBaseline()
    {
        var saved = QuickShellServices.Current.Shortcuts.GetByName(_originalName!);
        if (saved is null)
        {
            return;
        }

        _showingDiscardPrompt = false;
        _showRestoredDraftNote = false;
        _nameCustomized = false;
        _autoFilledName = null;

        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(saved);
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(saved, launchTarget);
        var companion = CompanionAppCatalog.ReconcileStoredShortcut(
            saved.OpenCompanionAppOnLaunch,
            saved.CompanionAppPath,
            saved.CompanionAppArguments);

        ApplyDraft(new FormDraft
        {
            OriginalName = saved.Name,
            Name = saved.Name,
            Abbreviation = saved.Abbreviation ?? string.Empty,
            Directory = saved.Directory,
            DevServerUrl = saved.DevServerUrl ?? string.Empty,
            RepoUrl = saved.RepoUrl ?? string.Empty,
            OpenCompanionAppOnLaunch = companion.LaunchOnWorkspaceOpen,
            CompanionAppPreset = companion.Preset,
            CompanionAppPath = companion.Path,
            CompanionAppArguments = companion.Arguments,
            Commands = commands,
            LaunchTarget = launchTarget,
            RunAsAdmin = saved.RunAsAdmin,
        }, persist: false);
        _baselineDraft = CloneDraft(_draft);
    }

    private void UnsubscribeFromDraftCleared()
    {
        if (!_subscribedToDraftCleared)
        {
            return;
        }

        if (_draftClearedHandler is not null)
        {
            QuickShellServices.Current.Drafts.Cleared -= _draftClearedHandler;
        }

        _subscribedToDraftCleared = false;
    }

    private void CaptureInputs(string payload)
    {
        if (!_baselineReady || _showingDiscardPrompt)
        {
            return;
        }

        if (MergeDraftFromInputs(payload, out var refreshForm))
        {
            if (refreshForm)
            {
                PublishDataJson(_draft);
            }

            PersistEditDraftIfNeeded();
        }
    }

    private void TryRestoreEditDraft()
    {
        if (_originalName is null)
        {
            return;
        }

        if (!QuickShellServices.Current.Drafts.TryGetForRestore(_originalName, out var persisted))
        {
            return;
        }

        var restored = ShortcutFormDraftData.FromPersisted(persisted);
        _showRestoredDraftNote = true;
        var commands = restored.Launches.Count > 0
            ? restored.Launches.Select(launch => new LaunchRowDraft
            {
                Id = string.IsNullOrWhiteSpace(launch.Id) ? Guid.NewGuid().ToString("N") : launch.Id,
                Command = launch.Command,
                TaskType = TaskTypeCatalog.Normalize(launch.TaskType),
                LaunchTarget = string.IsNullOrWhiteSpace(launch.LaunchTarget)
                    ? restored.LaunchTarget
                    : launch.LaunchTarget,
            }).ToList()
            : ShortcutFormLaunchSection.CommandsFromShortcut(null, restored.LaunchTarget);

        LaunchRowListEditor.EnsureMinimumRowsForEditor(commands, restored.LaunchTarget);

        if (commands.Count > 0 && restored.Launches.Count == 0 && !string.IsNullOrWhiteSpace(restored.Command))
        {
            commands[0].Command = restored.Command;
        }

        var companion = CompanionAppCatalog.ReconcileStoredShortcut(
            restored.OpenCompanionAppOnLaunch,
            restored.CompanionAppPath,
            restored.CompanionAppArguments);

        ApplyDraft(new FormDraft
        {
            OriginalName = restored.OriginalName,
            Name = restored.Name,
            Abbreviation = restored.Abbreviation,
            Directory = restored.Directory,
            DevServerUrl = restored.DevServerUrl,
            RepoUrl = restored.RepoUrl,
            OpenDevServerOnLaunch = restored.OpenDevServerOnLaunch,
            OpenCompanionAppOnLaunch = companion.LaunchOnWorkspaceOpen,
            CompanionAppPreset = companion.Preset,
            CompanionAppPath = companion.Path,
            CompanionAppArguments = companion.Arguments,
            Commands = commands,
            LaunchTarget = restored.LaunchTarget,
            RunAsAdmin = restored.RunAsAdmin,
        });
        _nameCustomized = persisted.NameCustomized;
        _autoFilledName = persisted.AutoFilledName;
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var payload = FormPayloadMerge.Merge(inputs, data);
        CaptureInputs(payload);

        if (IsDiscardPromptAction(payload, data))
        {
            return HandleDiscardPromptAction(payload, data);
        }

        if (IsHelpAction(payload, data))
        {
            return CommandResult.KeepOpen();
        }

        if (IsBrowseAction(payload, data))
        {
            return HandleBrowse(payload);
        }

        if (IsBrowseCompanionAppAction(payload, data))
        {
            return HandleBrowseCompanionApp(payload);
        }

        if (IsPasteAction(payload, data))
        {
            return HandlePaste(payload);
        }

        if (IsRefreshTerminalsAction(payload, data))
        {
            return HandleRefreshTerminals(payload);
        }

        if (IsAddSuggestedCommandAction(payload, data, out var pillCommand, out var pillTaskType, out var pillIndex))
        {
            return HandleAddSuggestedCommand(payload, pillCommand, pillTaskType, pillIndex);
        }

        if (IsClearLaunchAction(payload, data, out var clearIndex))
        {
            return HandleClearLaunch(payload, clearIndex);
        }

        if (IsExpandSuggestionPillsAction(payload, data))
        {
            return HandleExpandSuggestionPills(payload);
        }

        if (IsCollapseSuggestionPillsAction(payload, data))
        {
            return HandleCollapseSuggestionPills(payload);
        }

        if (IsCancelAction(payload, data))
        {
            return HandleCancel(payload);
        }

        return HandleSave(payload);
    }

    public override CommandResult SubmitForm(string payload)
    {
        CaptureInputs(payload);

        if (IsDiscardPromptAction(payload, null))
        {
            return HandleDiscardPromptAction(payload, null);
        }

        if (IsHelpAction(payload, null))
        {
            return CommandResult.KeepOpen();
        }

        if (IsBrowseAction(payload, null))
        {
            return HandleBrowse(payload);
        }

        if (IsBrowseCompanionAppAction(payload, null))
        {
            return HandleBrowseCompanionApp(payload);
        }

        if (IsPasteAction(payload, null))
        {
            return HandlePaste(payload);
        }

        if (IsRefreshTerminalsAction(payload, null))
        {
            return HandleRefreshTerminals(payload);
        }

        if (IsAddSuggestedCommandAction(payload, null, out var pillCommandFromPayload, out var pillTaskTypeFromPayload, out var pillIndexFromPayload))
        {
            return HandleAddSuggestedCommand(payload, pillCommandFromPayload, pillTaskTypeFromPayload, pillIndexFromPayload);
        }

        if (IsClearLaunchAction(payload, null, out var clearIndexFromPayload))
        {
            return HandleClearLaunch(payload, clearIndexFromPayload);
        }

        if (IsExpandSuggestionPillsAction(payload, null))
        {
            return HandleExpandSuggestionPills(payload);
        }

        if (IsCollapseSuggestionPillsAction(payload, null))
        {
            return HandleCollapseSuggestionPills(payload);
        }

        if (IsCancelAction(payload, null))
        {
            return HandleCancel(payload);
        }

        return HandleSave(payload);
    }

    public bool TryUndoEdit()
    {
        if (!_editHistory.TryUndo(CaptureEditSnapshot(), out var restored))
        {
            return false;
        }

        return ApplyEditSnapshot(restored);
    }

    public bool TryRedoEdit()
    {
        if (!_editHistory.TryRedo(CaptureEditSnapshot(), out var restored))
        {
            return false;
        }

        return ApplyEditSnapshot(restored);
    }

    private FormEditSnapshot CaptureEditSnapshot() =>
        new()
        {
            Commands = LaunchRowListEditor.CloneRows(_draft.Commands),
            ExpandSuggestionPills = _draft.ExpandSuggestionPills,
        };

    private void PushEditSnapshot() => _editHistory.PushBeforeChange(CaptureEditSnapshot());

    private bool ApplyEditSnapshot(FormEditSnapshot restored)
    {
        var previousCount = _draft.Commands.Count;
        _draft.Commands = restored.Commands;
        _draft.ExpandSuggestionPills = restored.ExpandSuggestionPills;
        ApplyDraft(_draft, forceTemplateRebuild: previousCount != restored.Commands.Count);
        return true;
    }

    private CommandResult HandleAddSuggestedCommand(
        string payload,
        string? pillCommand,
        string? pillTaskType,
        int pillIndex)
    {
        MergeDraftFromInputs(payload, out _);

        var pills = CommandSuggestionService.GetPills(
            _draft.Directory,
            _draft.Commands.Select(command => command.Command));

        var pill = CommandSuggestionService.TryFindPill(pills, pillCommand, pillTaskType);
        if (pill is null && pillIndex >= 0 && pillIndex < pills.Count)
        {
            pill = pills[pillIndex];
        }

        if (pill is null)
        {
            return QuickShellNavigation.StayOpen("That suggestion is no longer available.");
        }

        PushEditSnapshot();
        var rowAdded = CommandSuggestionService.ApplyPill(
            _draft.Commands,
            pill,
            GetDefaultRowLaunchTarget());

        ApplyDraft(_draft, forceTemplateRebuild: rowAdded);
        return QuickShellNavigation.StayOpen($"Added {pill.TypeTitle} command.");
    }

    private CommandResult HandleClearLaunch(string payload, int index)
    {
        MergeDraftFromInputs(payload, out _);
        if (index < 0 || index >= _draft.Commands.Count)
        {
            return QuickShellNavigation.StayOpen();
        }

        PushEditSnapshot();
        LaunchRowListEditor.ClearRow(_draft.Commands, index);
        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private CommandResult HandleExpandSuggestionPills(string payload)
    {
        MergeDraftFromInputs(payload, out _);
        PushEditSnapshot();
        _draft.ExpandSuggestionPills = true;
        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private CommandResult HandleCollapseSuggestionPills(string payload)
    {
        MergeDraftFromInputs(payload, out _);
        PushEditSnapshot();
        _draft.ExpandSuggestionPills = false;
        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private void RebuildTemplate(List<LaunchRowDraft> commands)
    {
        var terminalApplicationId =
            QuickShellServices.Current.Settings?.TerminalApplicationId ?? TerminalHostIds.WindowsTerminal;
        var commandCount = Math.Max(1, commands.Count);
        var companionChoicesJson = CompanionAppCatalog.BuildFormChoicesJson();
        const string templateSchemaKey = "suggestion-pills-v1";
        TemplateJson = ShortcutFormTemplateCache.GetOrBuild(
            commandCount,
            terminalApplicationId,
            companionChoicesJson,
            templateSchemaKey,
            () => ShortcutFormTemplateJson.BuildTemplate(
                FormTerminalChoicesJson(),
                companionChoicesJson,
                commands.Select(command => (command.Command, command.TaskType, command.LaunchTarget)).ToList(),
                QuickShellBrand.DisplayName));
    }

    private CommandResult HandleBrowseCompanionApp(string inputs)
    {
        MergeDraftFromInputs(inputs, out _);
        return TryBrowseCustomCompanion();
    }

    private CommandResult TryBrowseCustomCompanion()
    {
        var selected = ShortcutFilePickerService.PickExecutableFile();
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        var preset = CompanionAppCatalog.ResolvePresetAfterBrowse(selected);
        var args = CompanionAppArgumentValidation.NormalizeForSave(
            preset,
            selected,
            arguments: null);
        ApplyCompanionFormState(CompanionAppCatalog.ReconcileForForm(
            preset,
            selected,
            args));
        PublishDataJson(_draft);
        PersistEditDraftIfNeeded();
        return QuickShellNavigation.StayOpen();
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

        ApplyDraft(_draft, forceTemplateRebuild: true);
        return QuickShellNavigation.StayOpen(Strings.RefreshTerminals_Toast);
    }

    private CommandResult HandleBrowse(string inputs)
    {
        var initialDirectory = GetFieldFromPayload(inputs, "Directory") ?? _draft.Directory;
        MergeDraftFromInputs(inputs, out _, excludeDirectory: true);

        var selected = FolderPickerService.PickFolder(
            string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory);
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        ApplyDirectorySelection(selected);
        return QuickShellNavigation.StayOpen();
    }

    private CommandResult HandlePaste(string inputs)
    {
        MergeDraftFromInputs(inputs, out _, excludeDirectory: true);

        if (!TryReadClipboardFolderPath(out var pasted, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        ApplyDirectorySelection(pasted);
        return QuickShellNavigation.StayOpen();
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
            _draft.DevServerUrl = QuickShellServices.Current.ProjectAnalysis.TryDetectDevServerUrl(normalized) ?? string.Empty;
        }

        TryAutofillLaunchCommand(normalized);

        ApplyDraft(_draft, forceTemplateRebuild: true);
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

    private void TryAutofillLaunchCommand(string directory)
    {
        if (_draft.Commands.Count == 0)
        {
            _draft.Commands.Add(new LaunchRowDraft
        {
            LaunchTarget = GetDefaultRowLaunchTarget(),
        });
        }

        var firstCommand = _draft.Commands[0].Command;
        if (!ShouldAutofillLaunchCommand(firstCommand))
        {
            return;
        }

        var detected = WorkspaceSetupSuggestion.TryGetPrimaryCommand(directory);
        if (string.IsNullOrWhiteSpace(detected))
        {
            return;
        }

        _draft.Commands[0].Command = detected;
        _autoFilledLaunchCommand = detected;
    }

    private bool ShouldAutofillLaunchCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return true;
        }

        return _autoFilledLaunchCommand is not null
            && string.Equals(
                Normalize(command),
                Normalize(_autoFilledLaunchCommand),
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
            return LeaveShortcutForm();
        }

        if (!MergeDraftFromInputs(payload, out _))
        {
            return QuickShellNavigation.StayOpen(Strings.FormValues_ReadError);
        }

        if (!HasUnsavedChanges())
        {
            QuickShellServices.Current.Drafts.Clear();
            return LeaveShortcutForm();
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
            QuickShellServices.Current.Drafts.Clear();
            return LeaveShortcutForm();
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
            return QuickShellNavigation.StayOpen(Strings.FormValues_ReadError);
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

        if (!CompanionAppCatalog.TryValidateFormSelection(
                draft.CompanionAppPreset,
                draft.CompanionAppPath,
                out var companionSelectionError))
        {
            PersistEditDraftIfNeeded();
            return QuickShellNavigation.StayOpen(companionSelectionError);
        }

        if (!CompanionAppArgumentValidation.TryValidateForSave(
                draft.CompanionAppPreset,
                draft.CompanionAppPath,
                draft.CompanionAppArguments,
                out var companionArgumentError))
        {
            PersistEditDraftIfNeeded();
            return QuickShellNavigation.StayOpen(companionArgumentError);
        }

        draft.CompanionAppArguments = CompanionAppArgumentValidation.NormalizeForSave(
            draft.CompanionAppPreset,
            draft.CompanionAppPath,
            draft.CompanionAppArguments);

        ApplyCompanionFormState(CompanionAppCatalog.ReconcileForSave(
            draft.CompanionAppPreset,
            draft.CompanionAppPath,
            draft.CompanionAppArguments,
            draft.OpenCompanionAppOnLaunch));

        var result = ShortcutFormSave.TrySave(
            originalName,
            draft.Name,
            draft.Abbreviation,
            draft.Directory,
            ShortcutFormLaunchSection.ToLaunchInputs(
                draft.Commands,
                draft.Name,
                draft.LaunchTarget,
                draft.RunAsAdmin),
            QuickShellServices.Current.Shortcuts,
            onSaved: null,
            draft.DevServerUrl,
            draft.RepoUrl,
            draft.OpenDevServerOnLaunch,
            draft.OpenCompanionAppOnLaunch,
            draft.CompanionAppPath,
            draft.CompanionAppArguments);

        if (!result.Success)
        {
            PersistEditDraftIfNeeded();
            return QuickShellNavigation.StayOpen(result.Message);
        }

        QuickShellServices.Current.Drafts.Clear();
        SettingsFormHelpers.SchedulePostNavigationRefresh(_onSaved);
        return LeaveShortcutForm(result.Message);
    }

    private CommandResult LeaveShortcutForm(string? toastMessage = null)
    {
        UnsubscribeFromDraftCleared();
        _releaseForm?.Invoke();
        return QuickShellNavigation.ReturnToShortcutsList(toastMessage);
    }

    private void ApplyDraft(FormDraft draft, bool persist = true, bool forceTemplateRebuild = false)
    {
        _draft = draft;
        var commandCount = Math.Max(1, draft.Commands.Count);
        if (forceTemplateRebuild || _templateCommandCount != commandCount)
        {
            RebuildTemplate(draft.Commands);
            _templateCommandCount = commandCount;
        }

        PublishDataJson(draft);

        if (persist && _baselineReady)
        {
            PersistEditDraftIfNeeded();
        }
    }

    private void PublishDataJson(FormDraft draft) =>
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
                OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
                RunAsAdmin = draft.RunAsAdmin,
                ShowRestoredDraftNote = _showRestoredDraftNote,
                ExpandSuggestionPills = draft.ExpandSuggestionPills,
            },
            draft.Commands.Select(command => (command.Command, command.TaskType, command.LaunchTarget)).ToList());

    private void PersistEditDraftIfNeeded()
    {
        if (_originalName is null || _showingDiscardPrompt)
        {
            return;
        }

        QuickShellServices.Current.Drafts.SaveIfDirty(
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
            RunAsAdmin = draft.RunAsAdmin,
            Launches = draft.Commands.Select(command => new ShortcutFormLaunchDraftData
            {
                Id = command.Id,
                Command = command.Command,
                LaunchTarget = draft.LaunchTarget,
                RunAsAdmin = draft.RunAsAdmin,
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
        UpdateAutoFilledLaunchCommandTracking(data["LaunchCommand_0"]?.ToString());

        var previousPreset = _draft.CompanionAppPreset;
        var mergedPreset = data["CompanionAppPreset"]?.ToString() ?? _draft.CompanionAppPreset;

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
            OpenCompanionAppOnLaunch = _draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = mergedPreset,
            CompanionAppPath = _draft.CompanionAppPath,
            CompanionAppArguments = data["CompanionAppArguments"]?.ToString() ?? _draft.CompanionAppArguments,
            RunAsAdmin = ParseToggleBool(data["RunAsAdmin"]?.ToString(), _draft.RunAsAdmin),
        };

        refreshForm = ApplyCompanionPresetChange(previousPreset, mergedPreset);

        return true;
    }

    private bool ApplyCompanionPresetChange(string previousPreset, string mergedPreset)
    {
        if (string.Equals(previousPreset, mergedPreset, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ApplyCompanionFormState(CompanionAppCatalog.CreateStateFromPreset(mergedPreset));
        return true;
    }

    private static bool IsBrowseCompanionAppAction(string inputs, string? data) =>
        TryGetAction(data) == "browseCompanionApp"
        || TryGetActionFromInputs(inputs) == "browseCompanionApp";

    private static void ApplyCompanionFormState(FormDraft draft, CompanionAppCatalog.CompanionAppFormState state)
    {
        draft.CompanionAppPreset = state.Preset;
        draft.CompanionAppPath = state.Path;
        draft.CompanionAppArguments = state.Arguments;
        draft.OpenCompanionAppOnLaunch = state.LaunchOnWorkspaceOpen;
    }

    private void ApplyCompanionFormState(CompanionAppCatalog.CompanionAppFormState state) =>
        ApplyCompanionFormState(_draft, state);

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
            var command = data[$"LaunchCommand_{i}"]?.ToString() ?? prior.Command;
            merged.Add(new LaunchRowDraft
            {
                Id = prior.Id,
                Command = command,
                TaskType = TaskTypeCatalog.Normalize(data[$"LaunchType_{i}"]?.ToString() ?? prior.TaskType),
                LaunchTarget = data[$"LaunchTarget_{i}"]?.ToString()
                    ?? prior.LaunchTarget
                    ?? fallbackLaunchTarget,
                IsEditorPlaceholder = string.IsNullOrWhiteSpace(command) && prior.IsEditorPlaceholder,
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

    private void UpdateAutoFilledLaunchCommandTracking(string? mergedCommand)
    {
        mergedCommand ??= string.Empty;
        if (_autoFilledLaunchCommand is not null
            && !string.Equals(
                Normalize(mergedCommand),
                Normalize(_autoFilledLaunchCommand),
                StringComparison.OrdinalIgnoreCase))
        {
            _autoFilledLaunchCommand = null;
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

        return !string.IsNullOrWhiteSpace(pillCommand) || pillIndex >= 0;
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
            }).ToList(),
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
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
            || left.RunAsAdmin != right.RunAsAdmin)
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
                || !string.Equals(TaskTypeCatalog.Normalize(left.Commands[i].TaskType), TaskTypeCatalog.Normalize(right.Commands[i].TaskType), StringComparison.Ordinal))
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

        public bool ExpandSuggestionPills { get; set; }

        public FormEditSnapshot Clone() =>
            new()
            {
                Commands = LaunchRowListEditor.CloneRows(Commands),
                ExpandSuggestionPills = ExpandSuggestionPills,
            };
    }

    private static string FormTerminalChoicesJson() =>
        TerminalCatalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            QuickShellServices.Current.Settings?.TerminalApplicationId ?? TerminalHostIds.WindowsTerminal);

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
}
