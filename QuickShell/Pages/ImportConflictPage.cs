using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class ImportConflictPage : ContentPage
{
    public const string PageId = CommandDescriptor.ImportConflictId;

    public ImportConflictPage(Action onReload)
    {
        Id = PageId;
        Icon = new IconInfo("\uE7BA");
        Title = Strings.ImportConflictPage_Title;
        Name = Strings.ImportConflictPage_Name;
        _onReload = onReload;
    }

    private readonly Action _onReload;

    public override IContent[] GetContent() => [new ImportConflictForm(_onReload)];
}

internal sealed partial class ImportConflictForm : FormContent
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(30);

    private readonly Action _onReload;
    private readonly Action? _onSettingsChanged;

    public ImportConflictForm(Action onReload, Action? onSettingsChanged = null)
    {
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
              "text": "{{Escape(Strings.ImportConflictPage_Heading)}}",
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
              "text": "${FileName}",
              "isSubtle": true,
              "spacing": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "{{Escape(Strings.ImportConflictPage_ChooseOption)}}",
              "wrap": true,
              "weight": "Bolder",
              "spacing": "Large"
            },
            {
              "type": "TextBlock",
              "text": "{{Escape(Strings.ImportConflictPage_HelpText)}}",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small"
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ImportConflictPage_MergeButton)}}",
              "tooltip": "{{Escape(Strings.ImportConflictPage_MergeTooltip)}}",
              "data": { "action": "merge" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ImportConflictPage_ReplaceButton)}}",
              "tooltip": "{{Escape(Strings.ImportConflictPage_ReplaceTooltip)}}",
              "data": { "action": "replace" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ImportConflictPage_CancelButton)}}",
              "tooltip": "{{Escape(Strings.ImportConflictPage_CancelTooltip)}}",
              "data": { "action": "cancel" },
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
        if (action == "cancel")
        {
            ImportConflictState.Clear();
            SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
            return QuickShellNavigation.StayOnSettings(Strings.ImportConflictPage_Cancelled);
        }

        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return QuickShellNavigation.StayOnSettings(Strings.ImportConflictPage_NoImportPending);
        }

        var result = action switch
        {
            "merge" => ExecuteImportAction(pending, merge: true),
            "replace" => ExecuteImportAction(pending, merge: false),
            _ => null,
        };

        if (result is null)
        {
            return QuickShellNavigation.StayOnSettings(Strings.ImportConflictPage_UnableToReadForm);
        }

        if (!result.Success)
        {
            return QuickShellNavigation.StayOnSettings(result.Message);
        }

        ImportConflictState.Clear();
        _onReload();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.StayOnSettings(result.Message);
    }

    private void ApplyPendingState()
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            DataJson = $$"""
            {
              "Description": "{{Escape(Strings.ImportConflictPage_NoneWaiting)}}",
              "FileName": ""
            }
            """;
            return;
        }

        var fileName = Path.GetFileName(pending.Path);
        var conflictLabel = pending.ConflictCount == 1
            ? Strings.Word_Name_Singular
            : Strings.Word_Name_Plural;
        var importLabel = pending.ImportCount == 1
            ? Strings.Word_Workspace_Singular
            : Strings.Word_Workspace_Plural;
        var description = Strings.ImportConflictPage_DescriptionFormat(
            pending.ConflictCount, conflictLabel, pending.ImportCount, importLabel);

        DataJson = $$"""
        {
          "Description": "{{Escape(description)}}",
          "FileName": "{{Escape(fileName)}}"
        }
        """;
    }

    private static ShortcutTransferResult ExecuteImportAction(ImportConflictState.PendingImport pending, bool merge)
    {
        using var cancellation = new CancellationTokenSource(IoTimeout);
        return pending.Kind switch
        {
            ImportTransferKind.Projects => merge
                ? QuickShellServices.Current.Shortcuts.ImportMergeAsync(pending.Path, cancellation.Token).GetAwaiter().GetResult()
                : QuickShellServices.Current.Shortcuts.ImportReplaceAsync(pending.Path, cancellation.Token).GetAwaiter().GetResult(),
            _ => new ShortcutTransferResult { Success = false, Message = Strings.ImportConflictPage_UnknownImportType },
        };
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
