using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class ShortcutDetailsFormPage : ContentPage
{
    public ShortcutDetailsFormPage(
        TerminalShortcut shortcut,
        Action<TerminalShortcut> onChanged)
    {
        Id = $"com.quickshell.shortcut.details.{Guid.NewGuid():N}";
        Icon = new IconInfo("\uE70F");
        Title = "Workspace details";
        Name = "Edit";
        _shortcut = shortcut;
        _onChanged = onChanged;
    }

    private readonly TerminalShortcut _shortcut;
    private readonly Action<TerminalShortcut> _onChanged;

    public override IContent[] GetContent() =>
        [_form ??= new ShortcutDetailsForm(_shortcut, _onChanged, () => _form = null)];

    private ShortcutDetailsForm? _form;
}

internal sealed partial class ShortcutDetailsForm : FormContent
{
    private readonly TerminalShortcut _shortcut;
    private readonly Action<TerminalShortcut> _onChanged;
    private readonly Action? _releaseForm;
    private FormDraft _draft = new();

    public ShortcutDetailsForm(
        TerminalShortcut shortcut,
        Action<TerminalShortcut> onChanged,
        Action? releaseForm = null)
    {
        _shortcut = shortcut;
        _onChanged = onChanged;
        _releaseForm = releaseForm;
        TemplateJson = BuildTemplateJson();
        ApplyDraft();
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        CaptureInputs(inputs);

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
            _releaseForm?.Invoke();
            return QuickShellNavigation.GoBack();
        }

        return HandleSave(inputs);
    }

    public override CommandResult SubmitForm(string payload)
    {
        CaptureInputs(payload);

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
            _releaseForm?.Invoke();
            return QuickShellNavigation.GoBack();
        }

        return HandleSave(payload);
    }

    private CommandResult HandleBrowse(string inputs)
    {
        var initialDirectory = GetField(inputs, "Directory") ?? _draft.Directory;
        MergeDraftFromInputs(inputs, excludeDirectory: true);

        var selected = FolderPickerService.PickFolder(
            string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory);
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        return ApplyDirectorySelection(selected);
    }

    private CommandResult HandlePaste(string inputs)
    {
        MergeDraftFromInputs(inputs, excludeDirectory: true);

        if (!TryReadClipboardFolderPath(out var pasted, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        return ApplyDirectorySelection(pasted);
    }

    private CommandResult ApplyDirectorySelection(string directory)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out var error))
        {
            return QuickShellNavigation.StayOpen(error);
        }

        _draft.Directory = normalized;
        if (string.IsNullOrWhiteSpace(_draft.Name))
        {
            _draft.Name = DeriveNameFromDirectory(normalized);
        }

        PublishDraftJson();
        return QuickShellNavigation.StayOpen();
    }

    private CommandResult HandleSave(string inputs)
    {
        MergeDraftFromInputs(inputs);

        if (string.IsNullOrWhiteSpace(_draft.Directory))
        {
            return QuickShellNavigation.StayOpen("Folder path is required.");
        }

        if (!ShortcutValidation.TryNormalizeDirectory(_draft.Directory, out var normalized, out var directoryError))
        {
            return QuickShellNavigation.StayOpen(directoryError);
        }

        _shortcut.Name = _draft.Name.Trim();
        _shortcut.Abbreviation = string.IsNullOrWhiteSpace(_draft.Abbreviation) ? null : _draft.Abbreviation.Trim();
        _shortcut.Directory = normalized;
        _onChanged(_shortcut);
        _releaseForm?.Invoke();
        return QuickShellNavigation.GoBack("Workspace details updated.");
    }

    private void CaptureInputs(string payload)
    {
        _draft.Name = GetField(payload, "Name") ?? _draft.Name;
        _draft.Abbreviation = GetField(payload, "Abbreviation") ?? _draft.Abbreviation;
        _draft.Directory = GetField(payload, "Directory") ?? _draft.Directory;
    }

    private void MergeDraftFromInputs(string payload, bool excludeDirectory = false)
    {
        _draft.Name = GetField(payload, "Name") ?? _draft.Name;
        _draft.Abbreviation = GetField(payload, "Abbreviation") ?? _draft.Abbreviation;
        if (!excludeDirectory)
        {
            _draft.Directory = GetField(payload, "Directory") ?? _draft.Directory;
        }
    }

    private void ApplyDraft()
    {
        _draft.Name = _shortcut.Name;
        _draft.Abbreviation = _shortcut.Abbreviation ?? string.Empty;
        _draft.Directory = _shortcut.Directory;
        PublishDraftJson();
    }

    private void PublishDraftJson()
    {
        DataJson = $$"""
        {
          "Name": "{{EscapeJsonValue(_draft.Name)}}",
          "Abbreviation": "{{EscapeJsonValue(_draft.Abbreviation)}}",
          "Directory": "{{EscapeJsonValue(_draft.Directory)}}"
        }
        """;
    }

    private static string BuildTemplateJson() => $$"""
    {
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "type": "AdaptiveCard",
      "version": "1.6",
      "body": [
        {{SettingsCardJson.FieldGroup("Name", $"Shown in your {QuickShellBrand.DisplayName} list.", """
        {
          "type": "Input.Text",
          "id": "Name",
          "isRequired": true,
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
        {
          "type": "Container",
          "spacing": "Medium",
          "items": [
            {{SettingsCardJson.FieldLabel("Folder path")}},
            {{SettingsCardJson.FieldHelp("Folder opened when you run this workspace.")}},
            {
              "type": "Input.Text",
              "id": "Directory",
              "isRequired": true,
              "placeholder": "e.g. C:\\Projects\\MyApp",
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
                  "associatedInputs": "auto"
                },
                {
                  "type": "Action.Submit",
                  "title": "Paste path",
                  "data": { "action": "paste" },
                  "associatedInputs": "auto"
                }
              ]
            }
          ]
        }
      ],
      "actions": [
        {
          "type": "Action.Submit",
          "title": "Apply",
          "associatedInputs": "auto"
        },
        {
          "type": "Action.Submit",
          "title": "Cancel",
          "data": { "action": "cancel" },
          "associatedInputs": "none"
        }
      ]
    }
    """;

    private static bool IsBrowseAction(string inputs, string? data) =>
        TryGetAction(data) == "browse" || TryGetActionFromInputs(inputs) == "browse";

    private static bool IsPasteAction(string inputs, string? data) =>
        TryGetAction(data) == "paste" || TryGetActionFromInputs(inputs) == "paste";

    private static bool IsCancelAction(string inputs, string? data) =>
        data?.Contains("cancel", StringComparison.Ordinal) == true
        || TryGetActionFromInputs(inputs) == "cancel";

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data) ? null : JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();

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

        if (raw.Length >= 2
            && ((raw.StartsWith('"') && raw.EndsWith('"'))
                || (raw.StartsWith('\'') && raw.EndsWith('\''))))
        {
            raw = raw[1..^1].Trim();
        }

        if (!ShortcutValidation.TryNormalizeDirectory(raw, out var normalized, out var validationError))
        {
            error = validationError;
            return false;
        }

        path = normalized;
        return true;
    }

    private static string? GetField(string payload, string fieldName)
    {
        try
        {
            return JsonNode.Parse(payload)?[fieldName]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeJsonValue(string? value)
    {
        var encoded = JsonSerializer.Serialize(value ?? string.Empty, QuickShellJsonContext.Default.String);
        return encoded.Length >= 2 ? encoded[1..^1] : string.Empty;
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

    private sealed class FormDraft
    {
        public string Name { get; set; } = string.Empty;

        public string Abbreviation { get; set; } = string.Empty;

        public string Directory { get; set; } = string.Empty;
    }
}
