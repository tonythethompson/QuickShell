using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal partial class ShortcutFormPage : ContentPage
{
    private readonly TerminalShortcut? _existing;
    private readonly TerminalShortcut? _createSeed;
    private readonly Action? _onSaved;

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
            Commands = ShortcutContextCommands.BuildUndoRedoCommands(onSaved);
        }
    }

    public override IContent[] GetContent() =>
        [_form ??= new ShortcutForm(_existing, _createSeed, _onSaved, ReleaseForm)];

    private ShortcutForm? _form;

    private void ReleaseForm() => _form = null;

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
    private int _templateCommandCount = -1;

    public ShortcutForm(TerminalShortcut? existing, TerminalShortcut? createSeed, Action? onSaved, Action? releaseForm = null)
    {
        _originalName = existing?.Name;
        _onSaved = onSaved;
        _releaseForm = releaseForm;

        var initial = existing ?? createSeed;
        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(initial ?? new TerminalShortcut());
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(initial);

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
            QuickShellRuntimeServices.Drafts.Cleared += OnDraftStoreCleared;
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
        var saved = QuickShellRuntimeServices.Shortcuts.GetByName(_originalName!);
        if (saved is null)
        {
            return;
        }

        _showingDiscardPrompt = false;
        _showRestoredDraftNote = false;
        _nameCustomized = false;
        _autoFilledName = null;

        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(saved);
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(saved);
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

        QuickShellRuntimeServices.Drafts.Cleared -= OnDraftStoreCleared;
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

        if (!QuickShellRuntimeServices.Drafts.TryGetForRestore(_originalName, out var persisted))
        {
            return;
        }

        var restored = ShortcutFormDraftData.FromPersisted(persisted);
        _showRestoredDraftNote = true;
        var commands = restored.Launches.Count > 0
            ? restored.Launches.Select(launch => new ShortcutFormLaunchSection.CommandRowDraft
            {
                Id = string.IsNullOrWhiteSpace(launch.Id) ? Guid.NewGuid().ToString("N") : launch.Id,
                Command = launch.Command,
            }).ToList()
            : ShortcutFormLaunchSection.CommandsFromShortcut(null);

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
        CaptureInputs(inputs);

        if (IsDiscardPromptAction(inputs, data))
        {
            return HandleDiscardPromptAction(inputs, data);
        }

        if (IsHelpAction(inputs, data))
        {
            return CommandResult.KeepOpen();
        }

        if (IsBrowseAction(inputs, data))
        {
            return HandleBrowse(inputs);
        }

        if (IsBrowseCompanionAppAction(inputs, data))
        {
            return HandleBrowseCompanionApp(inputs);
        }

        if (IsPasteAction(inputs, data))
        {
            return HandlePaste(inputs);
        }

        if (IsRefreshTerminalsAction(inputs, data))
        {
            return HandleRefreshTerminals(inputs);
        }

        if (IsAddLaunchAction(inputs, data))
        {
            return HandleAddLaunch(inputs);
        }

        if (IsRemoveLaunchAction(inputs, data, out var removeIndex))
        {
            return HandleRemoveLaunch(inputs, removeIndex);
        }

        if (IsCancelAction(inputs, data))
        {
            return HandleCancel(inputs);
        }

        return HandleSave(inputs);
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

        if (IsAddLaunchAction(payload, null))
        {
            return HandleAddLaunch(payload);
        }

        if (IsRemoveLaunchAction(payload, null, out var removeIndexFromPayload))
        {
            return HandleRemoveLaunch(payload, removeIndexFromPayload);
        }

        if (IsCancelAction(payload, null))
        {
            return HandleCancel(payload);
        }

        return HandleSave(payload);
    }

    private CommandResult HandleAddLaunch(string inputs)
    {
        MergeDraftFromInputs(inputs, out _);
        _draft.Commands.Add(new ShortcutFormLaunchSection.CommandRowDraft());
        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen("Added command row.");
    }

    private CommandResult HandleRemoveLaunch(string inputs, int index)
    {
        MergeDraftFromInputs(inputs, out _);
        if (index >= 0 && index < _draft.Commands.Count && _draft.Commands.Count > 1)
        {
            _draft.Commands.RemoveAt(index);
        }

        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private void RebuildTemplate(List<ShortcutFormLaunchSection.CommandRowDraft> commands)
    {
        var terminalApplicationId =
            QuickShellRuntimeServices.Settings?.TerminalApplicationId ?? TerminalHostIds.WindowsTerminal;
        var commandCount = Math.Max(1, commands.Count);
        TemplateJson = ShortcutFormTemplateCache.GetOrBuild(
            commandCount,
            terminalApplicationId,
            () => ShortcutFormTemplateJson.BuildTemplate(
                FormTerminalChoicesJson(),
                CompanionAppCatalog.BuildFormChoicesJson(),
                commands.Select(command => command.Command).ToList(),
                QuickShellBrand.DisplayName));
    }

    private CommandResult HandleBrowseCompanionApp(string inputs)
    {
        MergeDraftFromInputs(inputs, out _);
        return TryBrowseCustomCompanion(_draft.CompanionAppPreset);
    }

    private CommandResult TryBrowseCustomCompanion(string revertPreset)
    {
        var selected = ShortcutFilePickerService.PickExecutableFile();
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        var args = string.IsNullOrWhiteSpace(_draft.CompanionAppArguments)
            ? CompanionAppCatalog.GetDefaultArguments(CompanionAppCatalog.InferPresetFromPath(selected))
            : _draft.CompanionAppArguments;
        ApplyCompanionFormState(CompanionAppCatalog.ReconcileForForm(
            CompanionAppCatalog.PresetCustom,
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
        if (!targets.Any(t => t.Id.Equals(_draft.LaunchTarget, StringComparison.OrdinalIgnoreCase)))
        {
            _draft.LaunchTarget = "default";
        }

        ApplyDraft(_draft, forceTemplateRebuild: true);
        return QuickShellNavigation.StayOpen("Terminal list refreshed.");
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
            _draft.DevServerUrl = DevServerUrlDetection.TryDetectDevServerUrl(normalized) ?? string.Empty;
        }

        TryAutofillLaunchCommand(normalized);

        if (string.IsNullOrWhiteSpace(_draft.CompanionAppPath))
        {
            var suggestion = CompanionAppDetection.TrySuggestFromDirectory(normalized);
            if (suggestion is not null)
            {
                ApplyCompanionFormState(CompanionAppCatalog.ReconcileForForm(
                    suggestion.PresetId,
                    suggestion.ExecutablePath,
                    suggestion.Arguments));
            }
        }

        ApplyDraft(_draft);
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
            _draft.Commands.Add(new ShortcutFormLaunchSection.CommandRowDraft());
        }

        var firstCommand = _draft.Commands[0].Command;
        if (!ShouldAutofillLaunchCommand(firstCommand))
        {
            return;
        }

        var detected = DevServerUrlDetection.TryDetectDevLaunchCommand(directory);
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
            error = "Clipboard does not contain text to paste.";
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
            error = $"Directory not found: {normalized}";
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
            return QuickShellNavigation.StayOpen("Unable to read form values.");
        }

        if (!HasUnsavedChanges())
        {
            QuickShellRuntimeServices.Drafts.Clear();
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
            QuickShellRuntimeServices.Drafts.Clear();
            return LeaveShortcutForm();
        }

        if (action == "save")
        {
            return SaveCurrentDraft();
        }

        return QuickShellNavigation.StayOpen("Unable to read form values.");
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
            return QuickShellNavigation.StayOpen("Unable to read form values.");
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

        ApplyCompanionFormState(CompanionAppCatalog.ReconcileForSave(
            draft.CompanionAppPreset,
            draft.CompanionAppPath,
            draft.CompanionAppArguments));

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
            QuickShellRuntimeServices.Shortcuts,
            _onSaved,
            draft.DevServerUrl,
            draft.RepoUrl,
            draft.OpenCompanionAppOnLaunch,
            draft.CompanionAppPath,
            draft.CompanionAppArguments);

        if (!result.Success)
        {
            PersistEditDraftIfNeeded();
            return QuickShellNavigation.StayOpen(result.Message);
        }

        QuickShellRuntimeServices.Drafts.Clear();
        return LeaveShortcutForm(result.Message);
    }

    private CommandResult LeaveShortcutForm(string? toastMessage = null)
    {
        UnsubscribeFromDraftCleared();
        _releaseForm?.Invoke();
        return QuickShellNavigation.PopToShortcutsList(toastMessage);
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
                RunAsAdmin = draft.RunAsAdmin,
                ShowRestoredDraftNote = _showRestoredDraftNote,
            },
            draft.Commands.Select(command => command.Command).ToList());

    private void PersistEditDraftIfNeeded()
    {
        if (_originalName is null || _showingDiscardPrompt)
        {
            return;
        }

        QuickShellRuntimeServices.Drafts.SaveIfDirty(
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
            LaunchTarget = data["LaunchTarget"]?.ToString() ?? _draft.LaunchTarget,
            DevServerUrl = data["DevServerUrl"]?.ToString() ?? _draft.DevServerUrl,
            RepoUrl = data["RepoUrl"]?.ToString() ?? _draft.RepoUrl,
            OpenCompanionAppOnLaunch = _draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = mergedPreset,
            CompanionAppPath = _draft.CompanionAppPath,
            CompanionAppArguments = _draft.CompanionAppArguments,
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

        if (string.Equals(mergedPreset, CompanionAppCatalog.PresetCustom, StringComparison.OrdinalIgnoreCase))
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

    private static List<ShortcutFormLaunchSection.CommandRowDraft> MergeCommandsFromInputs(
        JsonObject data,
        List<ShortcutFormLaunchSection.CommandRowDraft> existing)
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

        var merged = new List<ShortcutFormLaunchSection.CommandRowDraft>();
        for (var i = 0; i < count; i++)
        {
            var prior = i < existing.Count ? existing[i] : new ShortcutFormLaunchSection.CommandRowDraft();
            merged.Add(new ShortcutFormLaunchSection.CommandRowDraft
            {
                Id = prior.Id,
                Command = data[$"LaunchCommand_{i}"]?.ToString() ?? prior.Command,
            });
        }

        return merged;
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

    private static bool IsAddLaunchAction(string inputs, string? data) =>
        TryGetAction(data) == "addLaunch" || TryGetActionFromInputs(inputs) == "addLaunch";

    private static bool IsRemoveLaunchAction(string inputs, string? data, out int index)
    {
        index = -1;
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (!string.Equals(action, "removeLaunch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = data ?? inputs;
        var node = JsonNode.Parse(source)?.AsObject();
        if (node?["launchIndex"] is null)
        {
            return false;
        }

        return int.TryParse(node["launchIndex"]?.ToString(), out index);
    }

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
            Commands = draft.Commands.Select(command => new ShortcutFormLaunchSection.CommandRowDraft
            {
                Id = command.Id,
                Command = command.Command,
            }).ToList(),
            LaunchTarget = draft.LaunchTarget,
            DevServerUrl = draft.DevServerUrl,
            RepoUrl = draft.RepoUrl,
            OpenCompanionAppOnLaunch = draft.OpenCompanionAppOnLaunch,
            CompanionAppPreset = draft.CompanionAppPreset,
            CompanionAppPath = draft.CompanionAppPath,
            CompanionAppArguments = draft.CompanionAppArguments,
            RunAsAdmin = draft.RunAsAdmin,
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
            if (!string.Equals(Normalize(left.Commands[i].Command), Normalize(right.Commands[i].Command), StringComparison.Ordinal))
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

        public string RepoUrl { get; set; } = string.Empty;

        public bool OpenCompanionAppOnLaunch { get; set; }

        public string CompanionAppPreset { get; set; } = CompanionAppCatalog.PresetNone;

        public string CompanionAppPath { get; set; } = string.Empty;

        public string CompanionAppArguments { get; set; } = string.Empty;

        public List<ShortcutFormLaunchSection.CommandRowDraft> Commands { get; set; } = [new ShortcutFormLaunchSection.CommandRowDraft()];

        public string LaunchTarget { get; set; } = "default";

        public bool RunAsAdmin { get; set; }
    }

    private static string FormTerminalChoicesJson() =>
        TerminalCatalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            QuickShellRuntimeServices.Settings?.TerminalApplicationId ?? TerminalHostIds.WindowsTerminal);

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
}
