using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class PendingShortcutEditPage : ContentPage
{
    public const string PageId = "com.quickshell.pending-shortcut-edit";

    private readonly Action _onReload;

    public PendingShortcutEditPage(Action onReload)
    {
        _onReload = onReload;
        Id = PageId;
        Icon = new IconInfo("\uE7BA");
        Title = "Unsaved workspace changes";
        Name = "Resume edit";
    }

    public override IContent[] GetContent() => [new PendingShortcutEditForm(_onReload)];
}

internal sealed partial class PendingShortcutEditForm : FormContent
{
    private readonly Action _onReload;
    private readonly Action? _onSettingsChanged;

    public PendingShortcutEditForm(Action onReload, Action? onSettingsChanged = null)
    {
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;

        TemplateJson = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "TextBlock",
              "text": "Unsaved workspace changes",
              "weight": "Bolder",
              "size": "Large"
            },
            {
              "type": "TextBlock",
              "text": "${Description}",
              "wrap": true,
              "spacing": "Small"
            },
            {
              "type": "TextBlock",
              "text": "Save applies the same validation as the edit form. If required fields are missing, fix them by editing the workspace again.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Medium"
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

        ApplyPendingState();
    }

    public override CommandResult SubmitForm(string inputs, string data) =>
        HandleSubmit(TryGetAction(data) ?? TryGetActionFromInputs(inputs));

    public override CommandResult SubmitForm(string payload) =>
        HandleSubmit(TryGetActionFromInputs(payload));

    private CommandResult HandleSubmit(string? action)
    {
        if (action == "discard")
        {
            QuickShellRuntimeServices.Drafts.Clear();
            _onReload();
            _onSettingsChanged?.Invoke();
            return QuickShellNavigation.StayOnSettings("Discarded unsaved workspace changes.");
        }

        if (action != "save")
        {
            return QuickShellNavigation.StayOnSettings("Unable to read form values.");
        }

        var pending = QuickShellRuntimeServices.Drafts.Pending;
        if (pending is null)
        {
            _onReload();
            _onSettingsChanged?.Invoke();
            return QuickShellNavigation.StayOnSettings("No unsaved workspace edit is pending.");
        }

        var result = QuickShellRuntimeServices.Drafts.TryCommitPending(_onReload);
        if (!result.Success)
        {
            return QuickShellNavigation.StayOnSettings(result.Message);
        }

        _onSettingsChanged?.Invoke();
        return QuickShellNavigation.StayOnSettings(result.Message);
    }

    private void ApplyPendingState()
    {
        var pending = QuickShellRuntimeServices.Drafts.Pending;
        if (pending is null)
        {
            DataJson = """
            {
              "Description": "No unsaved workspace edit is waiting for a decision."
            }
            """;
            return;
        }

        var description =
            $"You left editing \"{pending.OriginalName}\" with unsaved changes. " +
            "Save them to your workspaces, or discard them.";

        DataJson = $$"""
        {
          "Description": "{{Escape(description)}}"
        }
        """;
    }

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();

    private static string? TryGetAction(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        return JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
