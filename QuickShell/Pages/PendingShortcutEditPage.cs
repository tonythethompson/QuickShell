using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class PendingShortcutEditPage : ContentPage
{
    public const string PageId = CommandDescriptor.PendingShortcutEditId;

    private readonly IQuickShellServices _services;
    private readonly Action _onReload;

    public PendingShortcutEditPage(IQuickShellServices services, Action onReload)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _onReload = onReload;
        Id = PageId;
        Icon = new IconInfo("\uE7BA");
        Title = Strings.PendingEdit_Title;
        Name = Strings.PendingEdit_ResumeName;
    }

    public override IContent[] GetContent() => [new PendingShortcutEditForm(_services, _onReload)];
}

internal sealed partial class PendingShortcutEditForm : FormContent
{
    private readonly IQuickShellServices _services;
    private readonly Action _onReload;
    private readonly Action? _onSettingsChanged;

    public PendingShortcutEditForm(IQuickShellServices services, Action onReload, Action? onSettingsChanged = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;

        RebuildTemplate();

        ApplyPendingState();
    }

    private void RebuildTemplate()
    {
        TemplateJson = $$"""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "TextBlock",
              "text": "{{Escape(Strings.PendingEdit_Title)}}",
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
              "text": "{{Escape(Strings.PendingEdit_ValidationNote)}}",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Medium"
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.PendingEdit_SaveAndCloseButton)}}",
              "data": { "action": "save" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.Common_Discard)}}",
              "data": { "action": "discard" },
              "associatedInputs": "none"
            }
          ]
        }
        """;
    }

    public override CommandResult SubmitForm(string inputs, string data) =>
        HandleSubmit(TryGetAction(data) ?? TryGetActionFromInputs(inputs));

    public override CommandResult SubmitForm(string payload) =>
        HandleSubmit(TryGetActionFromInputs(payload));

    private CommandResult HandleSubmit(string? action)
    {
        if (action == "discard")
        {
            _services.Drafts.Clear();
            _onReload();
            _onSettingsChanged?.Invoke();
            return QuickShellNavigation.StayOnSettings(Strings.PendingEdit_Discarded);
        }

        if (action != "save")
        {
            return QuickShellNavigation.StayOnSettings(Strings.PendingEdit_UnableToReadForm);
        }

        var pending = _services.Drafts.Pending;
        if (pending is null)
        {
            _onReload();
            _onSettingsChanged?.Invoke();
            return QuickShellNavigation.StayOnSettings(Strings.PendingEdit_NonePending);
        }

        var result = _services.Drafts.TryCommitPending(onSaved: null);
        if (!result.Success)
        {
            return QuickShellNavigation.StayOnSettings(result.Message);
        }

        SettingsFormHelpers.SchedulePostNavigationRefresh(_services.CallbackQueue, _onReload);
        _onSettingsChanged?.Invoke();
        return QuickShellNavigation.StayOnSettings(result.Message);
    }

    private void ApplyPendingState()
    {
        var pending = _services.Drafts.Pending;
        if (pending is null)
        {
            DataJson = $$"""
            {
              "Description": "{{Escape(Strings.PendingEdit_NoneWaiting)}}"
            }
            """;
            return;
        }

        var description = Strings.PendingEdit_LeftEditingFormat(pending.OriginalName);

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

    private static string Escape(string value)
    {
        var serialized = JsonSerializer.Serialize(value, QuickShellJsonContext.Default.String);
        return serialized.Substring(1, serialized.Length - 2);
    }
}
