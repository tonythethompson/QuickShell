using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class ShortcutFormPage : ContentPage
{
    private readonly ShortcutForm _form;

    public ShortcutFormPage(TerminalShortcut? existing = null, Action? onSaved = null)
    {
        _form = new ShortcutForm(existing, onSaved);
        Icon = new IconInfo("\uE70F");
        Title = existing is null ? "Add shortcut" : $"Edit {existing.Name}";
        Name = existing is null ? "Create" : "Edit";
    }

    public override IContent[] GetContent() => [_form];
}

internal sealed partial class ShortcutForm : FormContent
{
    private readonly string? _originalName;
    private readonly Action? _onSaved;
    private FormDraft _draft = new();
    private FormDraft _baselineDraft = new();
    private string? _autoFilledName;

    public ShortcutForm(TerminalShortcut? existing, Action? onSaved)
    {
        _originalName = existing?.Name;
        _onSaved = onSaved;

        var launchTarget = TerminalCatalog.EncodeLaunchTargetId(existing ?? new TerminalShortcut());
        var terminalChoices = TerminalCatalog.BuildFormChoicesJson(includeDefaultChoice: true);

        TemplateJson = $$"""
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
              "type": "Input.Text",
              "id": "Name",
              "label": "Name",
              "errorMessage": "Name is required",
              "value": "${Name}",
              "spacing": "Medium"
            },
            {
              "type": "Input.Text",
              "id": "Abbreviation",
              "label": "Search keyword (optional)",
              "value": "${Abbreviation}",
              "spacing": "Medium"
            },
            {
              "type": "Input.Text",
              "id": "Directory",
              "label": "Folder path",
              "errorMessage": "Folder path is required",
              "placeholder": "Type or paste a path, e.g. C:\\Projects\\MyApp",
              "value": "${Directory}",
              "spacing": "Medium"
            },
            {
              "type": "ActionSet",
              "spacing": "Small",
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Paste path",
                  "tooltip": "Paste a folder path from the clipboard into the field above",
                  "data": { "action": "paste" },
                  "associatedInputs": "none"
                },
                {
                  "type": "Action.Submit",
                  "title": "Browse folder",
                  "tooltip": "Open the Windows folder picker (you can also type or paste a path above)",
                  "data": { "action": "browse" },
                  "associatedInputs": "none"
                }
              ]
            },
            {
              "type": "Input.Text",
              "id": "Command",
              "label": "Command (optional)",
              "value": "${Command}",
              "spacing": "Medium"
            },
            {
              "type": "Input.ChoiceSet",
              "id": "LaunchTarget",
              "label": "Terminal",
              "style": "compact",
              "value": "${LaunchTarget}",
              "choices": {{terminalChoices}},
              "spacing": "Medium"
            }
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
              "data": { "action": "cancel" },
              "associatedInputs": "auto"
            }
          ]
        }
        """;

        ApplyDraft(new FormDraft
        {
            OriginalName = existing?.Name ?? string.Empty,
            Name = existing?.Name ?? string.Empty,
            Abbreviation = existing?.Abbreviation ?? string.Empty,
            Directory = existing?.Directory ?? string.Empty,
            Command = existing?.Command ?? string.Empty,
            LaunchTarget = launchTarget,
        });
        _baselineDraft = CloneDraft(_draft);
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        if (IsBrowseAction(inputs, data))
        {
            return HandleBrowse(inputs);
        }

        if (IsPasteAction(inputs, data))
        {
            return HandlePaste(inputs);
        }

        if (IsCancelAction(inputs, data))
        {
            return HandleCancel(inputs);
        }

        return HandleSave(inputs);
    }

    public override CommandResult SubmitForm(string payload)
    {
        if (IsBrowseAction(payload, null))
        {
            return HandleBrowse(payload);
        }

        if (IsPasteAction(payload, null))
        {
            return HandlePaste(payload);
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
            return true;
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
        if (!MergeDraftFromInputs(payload))
        {
            return QuickShellNavigation.StayOpen("Unable to read form values.");
        }

        if (!HasUnsavedChanges())
        {
            return QuickShellNavigation.ReturnToShortcutsList();
        }

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = "Unsaved changes",
            Description = "Save your changes before leaving?",
            PrimaryCommand = new SaveShortcutDraftCommand(this)
            {
                Name = "Save",
            },
        });
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

        if (string.IsNullOrWhiteSpace(draft.Directory))
        {
            return QuickShellNavigation.StayOpen("Folder path is required.");
        }

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            draft.Name = DeriveNameFromDirectory(draft.Directory);
            _autoFilledName = draft.Name;
        }

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            return QuickShellNavigation.StayOpen("Name is required.");
        }

        var shortcut = new TerminalShortcut
        {
            Name = draft.Name.Trim(),
            Abbreviation = string.IsNullOrWhiteSpace(draft.Abbreviation) ? null : draft.Abbreviation.Trim(),
            Directory = draft.Directory.Trim(),
            Command = string.IsNullOrWhiteSpace(draft.Command) ? null : draft.Command,
        };

        TerminalCatalog.ApplyLaunchTargetId(shortcut, draft.LaunchTarget);

        if (!ShortcutValidation.TryValidate(shortcut, out var validationError))
        {
            return QuickShellNavigation.StayOpen(validationError);
        }

        var originalName = string.IsNullOrWhiteSpace(draft.OriginalName) ? _originalName : draft.OriginalName;

        if (!ShortcutValidation.TryValidateUniqueName(shortcut.Name, originalName, out validationError))
        {
            return QuickShellNavigation.StayOpen(validationError);
        }

        try
        {
            ShortcutStore.Upsert(shortcut, originalName);
            _onSaved?.Invoke();
            return QuickShellNavigation.ReturnToShortcutsList($"Saved shortcut '{shortcut.Name}'.");
        }
        catch (Exception ex)
        {
            return QuickShellNavigation.StayOpen($"Failed to save shortcut: {ex.Message}");
        }
    }

    private void ApplyDraft(FormDraft draft)
    {
        _draft = draft;
        DataJson = $$"""
        {
          "OriginalName": "{{Escape(draft.OriginalName)}}",
          "Name": "{{Escape(draft.Name)}}",
          "Abbreviation": "{{Escape(draft.Abbreviation)}}",
          "Directory": "{{Escape(draft.Directory)}}",
          "Command": "{{Escape(draft.Command)}}",
          "LaunchTarget": "{{Escape(draft.LaunchTarget)}}"
        }
        """;
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
        };

        return true;
    }

    private void UpdateAutoFilledNameTracking(string mergedName)
    {
        if (_autoFilledName is null)
        {
            return;
        }

        if (!string.Equals(
                Normalize(mergedName),
                Normalize(_autoFilledName),
                StringComparison.OrdinalIgnoreCase))
        {
            _autoFilledName = null;
        }
    }

    private static string? GetFieldFromPayload(string payload, string field) =>
        JsonNode.Parse(payload)?.AsObject()?[field]?.ToString();

    private static bool IsBrowseAction(string inputs, string? data) =>
        TryGetAction(data) == "browse" || TryGetActionFromInputs(inputs) == "browse";

    private static bool IsPasteAction(string inputs, string? data) =>
        TryGetAction(data) == "paste" || TryGetActionFromInputs(inputs) == "paste";

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

    private sealed partial class SaveShortcutDraftCommand : InvokableCommand
    {
        private readonly ShortcutForm _form;

        public SaveShortcutDraftCommand(ShortcutForm form) => _form = form;

        public override CommandResult Invoke() => _form.SaveCurrentDraft();
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
        };

    private static bool DraftEquals(FormDraft left, FormDraft right) =>
        string.Equals(Normalize(left.Name), Normalize(right.Name), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Abbreviation), Normalize(right.Abbreviation), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Directory), Normalize(right.Directory), StringComparison.Ordinal)
        && string.Equals(Normalize(left.Command), Normalize(right.Command), StringComparison.Ordinal)
        && string.Equals(Normalize(left.LaunchTarget), Normalize(right.LaunchTarget), StringComparison.Ordinal);

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string Escape(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class FormDraft
    {
        public string OriginalName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Abbreviation { get; set; } = string.Empty;

        public string Directory { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public string LaunchTarget { get; set; } = "default";
    }
}
