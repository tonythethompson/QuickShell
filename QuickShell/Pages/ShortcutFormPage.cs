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

    public override IContent[] GetContent() =>
    [
        new MarkdownContent("**Name** is the title in your list. **Search keyword** is an optional shortcut for the root Command Palette. Set a **folder** and optional **command**."),
        _form,
    ];
}

internal sealed partial class ShortcutForm : FormContent
{
    private readonly string? _originalName;
    private readonly Action? _onSaved;
    private FormDraft _draft = new();

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
              "isRequired": true,
              "errorMessage": "Name is required",
              "value": "${Name}",
              "inlineAction": {
                "type": "Action.Submit",
                "title": "Info",
                "tooltip": "Title shown in your shortcut list, for example My API project.",
                "data": { "action": "help" },
                "associatedInputs": "none"
              }
            },
            {
              "type": "Input.Text",
              "id": "Abbreviation",
              "label": "Search keyword (optional)",
              "value": "${Abbreviation}",
              "inlineAction": {
                "type": "Action.Submit",
                "title": "Info",
                "tooltip": "Short text typed in the root Command Palette to find this shortcut. Not the same as Name.",
                "data": { "action": "help" },
                "associatedInputs": "none"
              }
            },
            {
              "type": "Input.Text",
              "id": "Directory",
              "label": "Folder path",
              "errorMessage": "Directory is required",
              "value": "${Directory}",
              "inlineAction": {
                "type": "Action.Submit",
                "title": "Browse",
                "tooltip": "Choose the folder to open in the terminal.",
                "data": { "action": "browse" },
                "associatedInputs": "auto"
              }
            },
            {
              "type": "Input.Text",
              "id": "Command",
              "label": "Command to run (optional)",
              "value": "${Command}",
              "inlineAction": {
                "type": "Action.Submit",
                "title": "Info",
                "tooltip": "Command run after the folder opens, for example npm run dev or dotnet run.",
                "data": { "action": "help" },
                "associatedInputs": "none"
              }
            },
            {
              "type": "TextBlock",
              "text": "Shell used when opening this shortcut.",
              "isSubtle": true,
              "size": "Small",
              "spacing": "Small"
            },
            {
              "type": "Input.ChoiceSet",
              "id": "LaunchTarget",
              "label": "Terminal",
              "style": "compact",
              "value": "${LaunchTarget}",
              "choices": {{terminalChoices}}
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Save shortcut",
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
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        if (IsHelpAction(inputs, data))
        {
            return CommandResult.KeepOpen();
        }

        if (IsBrowseAction(inputs, data))
        {
            return HandleBrowse(inputs);
        }

        return HandleSave(inputs);
    }

    public override CommandResult SubmitForm(string payload)
    {
        if (IsHelpAction(payload, null))
        {
            return CommandResult.KeepOpen();
        }

        if (IsBrowseAction(payload, null))
        {
            return HandleBrowse(payload);
        }

        return HandleSave(payload);
    }

    private CommandResult HandleBrowse(string inputs)
    {
        MergeDraftFromInputs(inputs);

        var selected = FolderPickerService.PickFolder(_draft.Directory);
        if (selected is null)
        {
            return CommandResult.KeepOpen();
        }

        _draft.Directory = selected;
        ApplyDraft(_draft);
        return QuickShellNavigation.StayOpen();
    }

    private CommandResult HandleSave(string payload)
    {
        if (!MergeDraftFromInputs(payload))
        {
            return QuickShellNavigation.StayOpen("Unable to read form values.");
        }

        var draft = _draft;

        if (string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.Directory))
        {
            return QuickShellNavigation.StayOpen("Name and directory are required.");
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

    private bool MergeDraftFromInputs(string payload)
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

        _draft = new FormDraft
        {
            OriginalName = data["OriginalName"]?.ToString() ?? _draft.OriginalName,
            Name = data["Name"]?.ToString() ?? _draft.Name,
            Abbreviation = data["Abbreviation"]?.ToString() ?? _draft.Abbreviation,
            Directory = data["Directory"]?.ToString() ?? _draft.Directory,
            Command = data["Command"]?.ToString() ?? _draft.Command,
            LaunchTarget = data["LaunchTarget"]?.ToString() ?? _draft.LaunchTarget,
        };

        return true;
    }

    private static bool IsHelpAction(string inputs, string? data)
    {
        if (TryGetAction(data) == "help")
        {
            return true;
        }

        var inputObject = JsonNode.Parse(inputs)?.AsObject();
        return inputObject?["action"]?.ToString() == "help";
    }

    private static bool IsBrowseAction(string inputs, string? data)
    {
        if (TryGetAction(data) == "browse")
        {
            return true;
        }

        var inputObject = JsonNode.Parse(inputs)?.AsObject();
        return inputObject?["action"]?.ToString() == "browse";
    }

    private static string? TryGetAction(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        return JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();
    }

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
