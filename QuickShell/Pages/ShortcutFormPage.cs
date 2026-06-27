using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal partial class ShortcutFormPage : ContentPage
{
    private readonly TerminalShortcut? _existing;
    private readonly Action? _onSaved;

    public ShortcutFormPage(TerminalShortcut? existing = null, Action? onSaved = null)
    {
        _existing = existing is null ? null : CloneShortcut(existing);
        _onSaved = onSaved;
        Id = existing is null
            ? $"com.quickshell.shortcut-form.create.{Guid.NewGuid():N}"
            : $"com.quickshell.shortcut-form.edit.{Guid.NewGuid():N}";
        Icon = new IconInfo("\uE70F");
        Title = existing is null ? "Add shortcut" : $"Edit {existing.Name}";
        Name = existing is null ? "Create" : "Edit";

        if (onSaved is not null)
        {
            Commands = ShortcutContextCommands.BuildUndoRedoCommands(onSaved);
        }
    }

    public override IContent[] GetContent() =>
        [_form ??= new ShortcutForm(_existing, _onSaved, ReleaseForm)];

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

    public ShortcutForm(TerminalShortcut? existing, Action? onSaved, Action? releaseForm = null)
    {
        _originalName = existing?.Name;
        _onSaved = onSaved;
        _releaseForm = releaseForm;

        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(existing ?? new TerminalShortcut());
        TemplateJson = BuildTemplateJson(FormTerminalChoicesJson());

        ApplyDraft(new FormDraft
        {
            OriginalName = existing?.Name ?? string.Empty,
            Name = existing?.Name ?? string.Empty,
            Abbreviation = existing?.Abbreviation ?? string.Empty,
            Directory = existing?.Directory ?? string.Empty,
            Command = existing?.Command ?? string.Empty,
            LaunchTarget = launchTarget,
            RunAsAdmin = existing?.RunAsAdmin ?? false,
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
        ApplyDraft(new FormDraft
        {
            OriginalName = restored.OriginalName,
            Name = restored.Name,
            Abbreviation = restored.Abbreviation,
            Directory = restored.Directory,
            Command = restored.Command,
            LaunchTarget = restored.LaunchTarget,
            RunAsAdmin = persisted.RunAsAdmin,
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

        if (IsPasteAction(inputs, data))
        {
            return HandlePaste(inputs);
        }

        if (IsRefreshTerminalsAction(inputs, data))
        {
            return HandleRefreshTerminals(inputs);
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

        if (IsPasteAction(payload, null))
        {
            return HandlePaste(payload);
        }

        if (IsRefreshTerminalsAction(payload, null))
        {
            return HandleRefreshTerminals(payload);
        }

        if (IsCancelAction(payload, null))
        {
            return HandleCancel(payload);
        }

        return HandleSave(payload);
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

    private CommandResult HandleRefreshTerminals(string inputs)
    {
        MergeDraftFromInputs(inputs);

        TerminalCatalog.InvalidateCache();
        TemplateJson = BuildTemplateJson(FormTerminalChoicesJson());

        var targets = TerminalCatalog.GetLaunchTargets(includeDefaultChoice: true);
        if (!targets.Any(t => t.Id.Equals(_draft.LaunchTarget, StringComparison.OrdinalIgnoreCase)))
        {
            _draft.LaunchTarget = "default";
        }

        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen("Terminal list refreshed.");
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
            draft.Command,
            draft.LaunchTarget,
            draft.RunAsAdmin,
            _onSaved);

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
        DataJson = $$"""
        {
          "OriginalName": "{{Escape(draft.OriginalName)}}",
          "Name": "{{Escape(draft.Name)}}",
          "Abbreviation": "{{Escape(draft.Abbreviation)}}",
          "Directory": "{{Escape(draft.Directory)}}",
          "Command": "{{Escape(draft.Command)}}",
          "LaunchTarget": "{{Escape(draft.LaunchTarget)}}",
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

    private static ShortcutFormDraftData ToDraftData(FormDraft draft) =>
        new()
        {
            OriginalName = draft.OriginalName,
            Name = draft.Name,
            Abbreviation = draft.Abbreviation,
            Directory = draft.Directory,
            Command = draft.Command,
            LaunchTarget = draft.LaunchTarget,
            RunAsAdmin = draft.RunAsAdmin,
        };

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

        _draft = new FormDraft
        {
            OriginalName = data["OriginalName"]?.ToString() ?? _draft.OriginalName,
            Name = mergedName,
            Abbreviation = data["Abbreviation"]?.ToString() ?? _draft.Abbreviation,
            Directory = excludeDirectory
                ? _draft.Directory
                : data["Directory"]?.ToString() ?? _draft.Directory,
            Command = data["Command"]?.ToString() ?? _draft.Command,
            LaunchTarget = data["LaunchTarget"]?.ToString() ?? _draft.LaunchTarget,
            RunAsAdmin = ParseToggleBool(data["RunAsAdmin"]?.ToString(), _draft.RunAsAdmin),
        };

        return true;
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
            Command = draft.Command,
            LaunchTarget = draft.LaunchTarget,
            RunAsAdmin = draft.RunAsAdmin,
        };

    private static bool DraftEquals(FormDraft left, FormDraft right) =>
        string.Equals(Normalize(left.Name), Normalize(right.Name), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Abbreviation), Normalize(right.Abbreviation), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Directory), Normalize(right.Directory), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Command), Normalize(right.Command), StringComparison.Ordinal)
        && string.Equals(Normalize(left.LaunchTarget), Normalize(right.LaunchTarget), StringComparison.Ordinal)
        && left.RunAsAdmin == right.RunAsAdmin;

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string Escape(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string BuildTemplateJson(string terminalChoices) => $$"""
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
            {{SettingsCardJson.FieldGroup("Name", "Shown in your Quick Shell list. Leave blank to use the folder name.", """
            {
              "type": "Input.Text",
              "id": "Name",
              "value": "${Name}"
            }
            """)}},
            {{SettingsCardJson.FieldGroup("Home keyword (optional)", "Type this at Command Palette home to jump straight to this shortcut.", """
            {
              "type": "Input.Text",
              "id": "Abbreviation",
              "placeholder": "e.g. api",
              "value": "${Abbreviation}"
            }
            """)}},
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {{SettingsCardJson.FieldLabel("Folder path")}},
                {{SettingsCardJson.FieldHelp("Folder opened when you run this shortcut.")}},
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
            {{SettingsCardJson.FieldGroup("Command (optional)", "Optional command or script run after the terminal opens in this folder.", """
            {
              "type": "Input.Text",
              "id": "Command",
              "value": "${Command}"
            }
            """)}},
            {
              "type": "Container",
              "spacing": "Medium",
              "items": [
                {{SettingsCardJson.FieldLabel("Terminal profile")}},
                {{SettingsCardJson.FieldHelp("Profiles from Windows Terminal, WSL, and classic shells.")}},
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
              "title": "Save shortcut",
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

    private static string FormTerminalChoicesJson() =>
        TerminalCatalog.BuildFormChoicesJson(
            includeDefaultChoice: true,
            QuickShellRuntimeServices.Settings?.TerminalApplicationId ?? TerminalHostIds.WindowsTerminal);

    private sealed class FormDraft
    {
        public string OriginalName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Abbreviation { get; set; } = string.Empty;

        public string Directory { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public string LaunchTarget { get; set; } = "default";

        public bool RunAsAdmin { get; set; }
    }

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
}
