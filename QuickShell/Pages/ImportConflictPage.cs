using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class ImportConflictPage : ContentPage
{
    public const string PageId = "com.quickshell.import-conflict";

    public ImportConflictPage(Action onReload)
    {
        Id = PageId;
        Icon = new IconInfo("\uE7BA");
        Title = "Finish importing";
        Name = "Import";
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

        TemplateJson = """
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.6",
          "body": [
            {
              "type": "TextBlock",
              "text": "Duplicate shortcut names",
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
              "text": "Choose an option below to complete the import.",
              "wrap": true,
              "weight": "Bolder",
              "spacing": "Large"
            },
            {
              "type": "TextBlock",
              "text": "Merge keeps your shortcuts and adds the file; duplicate names are renamed (for example \"My App Copy\"). Replace all deletes every current shortcut and loads only the file.",
              "wrap": true,
              "isSubtle": true,
              "spacing": "Small"
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Merge (rename duplicates)",
              "tooltip": "Keep your shortcuts and add imported ones. Duplicate names become \"Name Copy\", \"Name Copy 2\", and so on.",
              "data": { "action": "merge" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "Replace all shortcuts",
              "tooltip": "Delete every shortcut you have now (including favorites) and replace them with the imported file only.",
              "data": { "action": "replace" },
              "associatedInputs": "none"
            },
            {
              "type": "Action.Submit",
              "title": "Cancel import",
              "tooltip": "Discard this import file and keep your shortcuts unchanged.",
              "data": { "action": "cancel" },
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
        if (action == "cancel")
        {
            ImportConflictState.Clear();
            SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
            return QuickShellNavigation.StayOnSettings("Import cancelled.");
        }

        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return QuickShellNavigation.StayOnSettings("No import is pending.");
        }

        var result = action switch
        {
            "merge" => ExecuteImportAction(token => QuickShellRuntimeServices.Shortcuts.ImportMergeAsync(pending.Path, token)),
            "replace" => ExecuteImportAction(token => QuickShellRuntimeServices.Shortcuts.ImportReplaceAsync(pending.Path, token)),
            _ => null,
        };

        if (result is null)
        {
            return QuickShellNavigation.StayOnSettings("Unable to read form values.");
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
            DataJson = """
            {
              "Description": "No import is waiting for a decision.",
              "FileName": ""
            }
            """;
            return;
        }

        var fileName = Path.GetFileName(pending.Path);
        var conflictLabel = pending.ConflictCount == 1 ? "name" : "names";
        var importLabel = pending.ImportCount == 1 ? "shortcut" : "shortcuts";
        var description =
            $"The file contains {pending.ConflictCount} duplicate {conflictLabel} " +
            $"among {pending.ImportCount} {importLabel}.";

        DataJson = $$"""
        {
          "Description": "{{Escape(description)}}",
          "FileName": "{{Escape(fileName)}}"
        }
        """;
    }

    private static ShortcutTransferResult ExecuteImportAction(Func<CancellationToken, Task<ShortcutTransferResult>> action)
    {
        using var cancellation = new CancellationTokenSource(IoTimeout);
        return action(cancellation.Token).GetAwaiter().GetResult();
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
