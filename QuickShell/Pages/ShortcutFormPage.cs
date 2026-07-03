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
            : $"com.quickshell.shortcut-form.edit.{Guid.NewGuid():N}";
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
        OpenDevServerOnLaunch = shortcut.OpenDevServerOnLaunch,
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
    private bool _nameCustomized;
    private bool _showingDiscardPrompt;
    private bool _baselineReady;
    private bool _showRestoredDraftNote;

    public ShortcutForm(TerminalShortcut? existing, TerminalShortcut? createSeed, Action? onSaved, Action? releaseForm = null)
    {
        _originalName = existing?.Name;
        _onSaved = onSaved;
        _releaseForm = releaseForm;

        var initial = existing ?? createSeed;
        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(initial ?? new TerminalShortcut());
        var commands = ShortcutFormLaunchSection.CommandsFromShortcut(initial);
        RebuildTemplate(commands);

        ApplyDraft(new FormDraft
        {
            OriginalName = existing?.Name ?? string.Empty,
            Name = initial?.Name ?? string.Empty,
            Abbreviation = initial?.Abbreviation ?? string.Empty,
            Directory = initial?.Directory ?? string.Empty,
            DevServerUrl = initial?.DevServerUrl ?? string.Empty,
            RepoUrl = initial?.RepoUrl ?? string.Empty,
            OpenDevServerOnLaunch = initial?.OpenDevServerOnLaunch ?? false,
            OpenCompanionAppOnLaunch = initial?.OpenCompanionAppOnLaunch ?? false,
            CompanionAppPreset = CompanionAppCatalog.InferPresetFromPath(initial?.CompanionAppPath),
            CompanionAppPath = initial?.CompanionAppPath ?? string.Empty,
            CompanionAppArguments = initial?.CompanionAppArguments ?? string.Empty,
            Commands = commands,
            LaunchTarget = launchTarget,
            RunAsAdmin = initial?.RunAsAdmin ?? false,
        }, persist: false);
        _baselineDraft = CloneDraft(_draft);
        _baselineReady = true;
        TryRestoreEditDraft();
    }

    private void CaptureInputs(string payload)
    {
        if (!_baselineReady || _showingDiscardPrompt)
        {
            return;
        }

        if (MergeDraftFromInputs(payload))
        {
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

        ApplyDraft(new FormDraft
        {
            OriginalName = restored.OriginalName,
            Name = restored.Name,
            Abbreviation = restored.Abbreviation,
            Directory = restored.Directory,
            DevServerUrl = restored.DevServerUrl,
            RepoUrl = restored.RepoUrl,
            OpenDevServerOnLaunch = restored.OpenDevServerOnLaunch,
            OpenCompanionAppOnLaunch = restored.OpenCompanionAppOnLaunch,
            CompanionAppPreset = restored.CompanionAppPreset,
            CompanionAppPath = restored.CompanionAppPath,
            CompanionAppArguments = restored.CompanionAppArguments,
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

        if (IsBrowseAppAction(inputs, data))
        {
            return HandleBrowseApp(inputs);
        }

        if (IsBrowseAction(inputs, data))
        {
            return HandleBrowse(inputs);
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

        if (IsBrowseAppAction(payload, null))
        {
            return HandleBrowseApp(payload);
        }

        if (IsBrowseAction(payload, null))
        {
            return HandleBrowse(payload);
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
        MergeDraftFromInputs(inputs);
        _draft.Commands.Add(new ShortcutFormLaunchSection.CommandRowDraft());
        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen("Added command row.");
    }

    private CommandResult HandleRemoveLaunch(string inputs, int index)
    {
        MergeDraftFromInputs(inputs);
        if (index >= 0 && index < _draft.Commands.Count && _draft.Commands.Count > 1)
        {
            _draft.Commands.RemoveAt(index);
        }

        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private void RebuildTemplate(IReadOnlyList<ShortcutFormLaunchSection.CommandRowDraft> commands) =>
        TemplateJson = BuildTemplateJson(
            FormTerminalChoicesJson(),
            CompanionAppCatalog.BuildFormChoicesJson(),
            commands);

    private CommandResult HandleRefreshTerminals(string inputs)
    {
        MergeDraftFromInputs(inputs);

        TerminalCatalog.InvalidateCache();

        var targets = TerminalCatalog.GetLaunchTargets(includeDefaultChoice: true);
        if (!targets.Any(t => t.Id.Equals(_draft.LaunchTarget, StringComparison.OrdinalIgnoreCase)))
        {
            _draft.LaunchTarget = "default";
        }

        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen("Terminal list refreshed.");
    }

    private CommandResult HandleBrowseApp(string inputs)
    {
        MergeDraftFromInputs(inputs);

        var selected = ShortcutFilePickerService.PickExecutableFile();
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        _draft.CompanionAppPath = selected;
        _draft.CompanionAppPreset = CompanionAppCatalog.PresetCustom;
        if (string.IsNullOrWhiteSpace(_draft.CompanionAppArguments))
        {
            _draft.CompanionAppArguments = CompanionAppCatalog.GetDefaultArguments(
                CompanionAppCatalog.InferPresetFromPath(selected));
        }

        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private CommandResult HandleBrowse(string inputs)
    {
        var initialDirectory = GetFieldFromPayload(inputs, "Directory") ?? _draft.Directory;
        MergeDraftFromInputs(inputs, excludeDirectory: true);

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
        MergeDraftFromInputs(inputs, excludeDirectory: true);

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

        if (string.IsNullOrWhiteSpace(_draft.CompanionAppPath))
        {
            var suggestion = CompanionAppDetection.TrySuggestFromDirectory(normalized);
            if (suggestion is not null)
            {
                _draft.CompanionAppPreset = suggestion.PresetId;
                _draft.CompanionAppPath = suggestion.ExecutablePath ?? string.Empty;
                _draft.CompanionAppArguments = suggestion.Arguments;
                _draft.OpenCompanionAppOnLaunch = suggestion.EnableOnLaunch;
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

        if (!MergeDraftFromInputs(payload))
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
        TemplateJson = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "TextBlock",
              "text": "Unsaved changes",
              "weight": "Bolder",
              "size": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "Save your changes, or discard them and leave?",
              "wrap": true
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Save and close",
              "data": { "action": "save" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "Discard",
              "data": { "action": "discard" },
              "associatedInputs": "none"
            }
          ]
        }
        """;
        DataJson = "{}";
    }

    private CommandResult HandleSave(string payload)
    {
        if (!MergeDraftFromInputs(payload))
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
            draft.OpenDevServerOnLaunch,
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
        _releaseForm?.Invoke();
        return QuickShellNavigation.PopToShortcutsList(toastMessage);
    }

    private void ApplyDraft(FormDraft draft, bool persist = true)
    {
        _draft = draft;
        RebuildTemplate(draft.Commands);
        DataJson = $$"""
        {
          "OriginalName": "{{Escape(draft.OriginalName)}}",
          "Name": "{{Escape(draft.Name)}}",
          "Abbreviation": "{{Escape(draft.Abbreviation)}}",
          "Directory": "{{Escape(draft.Directory)}}",
          "LaunchTarget": "{{Escape(draft.LaunchTarget)}}",
          "DevServerUrl": "{{Escape(draft.DevServerUrl)}}",
          "RepoUrl": "{{Escape(draft.RepoUrl)}}",
          "OpenDevServerOnLaunch": "{{(draft.OpenDevServerOnLaunch ? "true" : "false")}}",
          "OpenCompanionAppOnLaunch": "{{(draft.OpenCompanionAppOnLaunch ? "true" : "false")}}",
          "CompanionAppPreset": "{{Escape(draft.CompanionAppPreset)}}",
          "CompanionAppPath": "{{Escape(draft.CompanionAppPath)}}",
          "CompanionAppArguments": "{{Escape(draft.CompanionAppArguments)}}",
          "RunAsAdmin": "{{(draft.RunAsAdmin ? "true" : "false")}}",
          "ShowRestoredDraftNote": {{(_showRestoredDraftNote ? "true" : "false")}}
        }
        """;

        if (persist && _baselineReady)
        {
            PersistEditDraftIfNeeded();
        }
    }

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
            }).ToList(),
        };
    }

    private bool HasUnsavedChanges() => !DraftEquals(_draft, _baselineDraft);

    private bool MergeDraftFromInputs(string payload, bool excludeDirectory = false)
    {
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
            OpenDevServerOnLaunch = ParseToggleBool(
                data["OpenDevServerOnLaunch"]?.ToString(),
                _draft.OpenDevServerOnLaunch),
            OpenCompanionAppOnLaunch = ParseToggleBool(
                data["OpenCompanionAppOnLaunch"]?.ToString(),
                _draft.OpenCompanionAppOnLaunch),
            CompanionAppPreset = mergedPreset,
            CompanionAppPath = data["CompanionAppPath"]?.ToString() ?? _draft.CompanionAppPath,
            CompanionAppArguments = data["CompanionAppArguments"]?.ToString() ?? _draft.CompanionAppArguments,
            RunAsAdmin = ParseToggleBool(data["RunAsAdmin"]?.ToString(), _draft.RunAsAdmin),
        };

        ApplyCompanionPresetChange(previousPreset, mergedPreset);

        if (string.IsNullOrWhiteSpace(_draft.DevServerUrl))
        {
            _draft.OpenDevServerOnLaunch = false;
        }

        return true;
    }

    private void ApplyCompanionPresetChange(string previousPreset, string mergedPreset)
    {
        if (string.Equals(previousPreset, mergedPreset, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(mergedPreset, CompanionAppCatalog.PresetNone, StringComparison.OrdinalIgnoreCase))
        {
            _draft.CompanionAppPath = string.Empty;
            _draft.CompanionAppArguments = string.Empty;
            _draft.OpenCompanionAppOnLaunch = false;
            return;
        }

        if (string.Equals(mergedPreset, CompanionAppCatalog.PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (CompanionAppCatalog.TryApplyPreset(mergedPreset, out var executablePath, out var arguments))
        {
            _draft.CompanionAppPath = executablePath ?? string.Empty;
            _draft.CompanionAppArguments = arguments;
        }
    }

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

    private static bool IsBrowseAppAction(string inputs, string? data) =>
        TryGetAction(data) == "browseApp" || TryGetActionFromInputs(inputs) == "browseApp";

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
            OpenDevServerOnLaunch = draft.OpenDevServerOnLaunch,
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
            || left.OpenDevServerOnLaunch != right.OpenDevServerOnLaunch
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

    private static string Escape(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string BuildTemplateJson(
        string terminalChoices,
        string companionChoices,
        IReadOnlyList<ShortcutFormLaunchSection.CommandRowDraft> commands)
    {
        var commandRows = ShortcutFormLaunchSection.BuildCommandRowsJson(commands);
        return $$"""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "Input.Text",
              "id": "OriginalName",
              "isVisible": false,
              "value": "${OriginalName}"
            },
            {
              "type": "TextBlock",
              "text": "Restored unsaved changes from your last edit. Save or Cancel when you are done.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small",
              "$when": "${ShowRestoredDraftNote}"
            },
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {{SettingsCardJson.FieldLabel("Folder path")}},
                {{SettingsCardJson.FieldHelp("Folder opened when you run this workspace. Browse or paste to pick a folder.")}},
                {
                  "type": "Input.Text",
                  "id": "Directory",
                  "isRequired": true,
                  "errorMessage": "Folder path is required",
                  "placeholder": "Type or paste a path, e.g. C:\\Projects\\MyApp",
                  "value": "${Directory}"
                },
                {
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Browse folder",
                      "data": { "action": "browse" },
                      "associatedInputs": "none"
                    },
                    {
                      "type": "Action.Submit",
                      "title": "Paste path",
                      "data": { "action": "paste" },
                      "associatedInputs": "none"
                    }
                  ]
                }
              ]
            },
            {{SettingsCardJson.FieldGroup("Name", $"Shown in your {QuickShellBrand.DisplayName} list. Filled in from the folder name when you browse or paste—you can edit it.", """
            {
              "type": "Input.Text",
              "id": "Name",
              "value": "${Name}"
            }
            """)}},
            {{SettingsCardJson.FieldGroup("Home keyword (optional)", "Type this at Command Palette home to jump straight to this workspace.", """
            {
              "type": "Input.Text",
              "id": "Abbreviation",
              "placeholder": "e.g. api",
              "value": "${Abbreviation}"
            }
            """)}},
            {{SettingsCardJson.FieldGroup("Dev server URL (optional)", "Used by the action menu and optional auto-open on workspace launch, e.g. http://localhost:3000.", """
            {
              "type": "Input.Text",
              "id": "DevServerUrl",
              "placeholder": "http://localhost:3000",
              "value": "${DevServerUrl}"
            }
            """)}},
            {{SettingsCardJson.FieldGroup("Open on workspace launch", "Opens the dev server URL in your browser when you run the full workspace.", """
            {
              "type": "Input.Toggle",
              "id": "OpenDevServerOnLaunch",
              "title": "Open dev server when workspace runs",
              "value": "${OpenDevServerOnLaunch}",
              "valueOn": "true",
              "valueOff": "false"
            }
            """)}},
            {{SettingsCardJson.FieldGroup("Repository URL (optional)", "Opens from the workspace action menu, e.g. your GitHub repo page.", """
            {
              "type": "Input.Text",
              "id": "RepoUrl",
              "placeholder": "https://github.com/you/your-repo",
              "value": "${RepoUrl}"
            }
            """)}},
            {
              "type": "TextBlock",
              "text": "Companion app",
              "weight": "Bolder",
              "spacing": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "Optionally open an editor or other app with this workspace folder when you run the workspace.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small"
            },
            {{SettingsCardJson.FieldGroup("Open on workspace launch", "Runs alongside your terminals when you open the full workspace.", """
            {
              "type": "Input.Toggle",
              "id": "OpenCompanionAppOnLaunch",
              "title": "Open companion app when workspace runs",
              "value": "${OpenCompanionAppOnLaunch}",
              "valueOn": "true",
              "valueOff": "false"
            }
            """)}},
            {
              "type": "Container",
              "spacing": "Small",
              "items": [
                {{SettingsCardJson.FieldLabel("App preset")}},
                {{SettingsCardJson.FieldHelp("Pick a common editor or choose Custom to browse for any executable.")}},
                {
                  "type": "Input.ChoiceSet",
                  "id": "CompanionAppPreset",
                  "style": "compact",
                  "value": "${CompanionAppPreset}",
                  "choices": {{companionChoices}}
                }
              ]
            },
            {
              "type": "Container",
              "spacing": "Small",
              "items": [
                {{SettingsCardJson.FieldLabel("Executable")}},
                {
                  "type": "Input.Text",
                  "id": "CompanionAppPath",
                  "placeholder": "C:\\Users\\you\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe",
                  "value": "${CompanionAppPath}"
                },
                {
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Browse app…",
                      "associatedInputs": "auto",
                      "data": { "action": "browseApp" }
                    }
                  ]
                }
              ]
            },
            {{SettingsCardJson.FieldGroup("Arguments (optional)", "Use . or {folder} for the workspace folder. VS Code and Cursor default to .", """
            {
              "type": "Input.Text",
              "id": "CompanionAppArguments",
              "placeholder": ".",
              "value": "${CompanionAppArguments}"
            }
            """)}},
            {
              "type": "TextBlock",
              "text": "Commands",
              "weight": "Bolder",
              "spacing": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "Each command uses this workspace's terminal. Leave blank to open the folder only.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small"
            },
            {{commandRows}},
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {{SettingsCardJson.FieldLabel("Terminal profile")}},
                {{SettingsCardJson.FieldHelp("Applies to every command in this workspace.")}},
                {
                  "type": "Input.ChoiceSet",
                  "id": "LaunchTarget",
                  "style": "compact",
                  "value": "${LaunchTarget}",
                  "choices": {{terminalChoices}}
                },
                {
                  "type": "ActionSet",
                  "spacing": "Small",
                  "actions": [
                    {
                      "type": "Action.Submit",
                      "title": "Refresh profile list",
                      "tooltip": "Reload after installing a shell or editing Windows Terminal settings.",
                      "associatedInputs": "auto",
                      "data": { "action": "refreshTerminals" }
                    }
                  ]
                }
              ]
            },
            {{SettingsCardJson.FieldGroup("Administrator", "Launch elevated. Windows may show a UAC prompt each time.", """
            {
              "type": "Input.Toggle",
              "id": "RunAsAdmin",
              "title": "Always run as administrator",
              "value": "${RunAsAdmin}",
              "valueOn": "true",
              "valueOff": "false"
            }
            """)}}
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Save workspace",
              "associatedInputs": "auto"
            },
            {
              "type": "Action.Submit",
              "title": "Cancel",
              "tooltip": "Unsaved changes prompt you before leaving.",
              "data": { "action": "cancel" },
              "associatedInputs": "none"
            }
          ]
        }
        """;
    }

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
