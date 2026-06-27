using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class ShortcutTransferSettingsForm : FormContent
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(30);

    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;

    public ShortcutTransferSettingsForm(Action? onReload, Action? onSettingsChanged = null)
    {
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        var result = action switch
        {
            "export" => RunExport(),
            "import" => new ImportShortcutsCommand(
                _onReload ?? (() => { }),
                stayOnSettings: true,
                onSettingsRefresh: _onSettingsChanged).Invoke(),
            "merge" => ResolveImportConflict(merge: true),
            "replace" => ResolveImportConflict(merge: false),
            "cancel" => CancelImportConflict(),
            _ => CommandResult.KeepOpen(),
        };

        return result;
    }

    private CommandResult RunExport()
    {
        var result = new ExportShortcutsCommand(stayOnSettings: true).Invoke();
        RebuildTemplate();
        return result;
    }

    private CommandResult ResolveImportConflict(bool merge)
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return Finish("No import is pending.");
        }

        var transferResult = ExecuteImportAction(token => merge
            ? QuickShellRuntimeServices.Shortcuts.ImportMergeAsync(pending.Path, token)
            : QuickShellRuntimeServices.Shortcuts.ImportReplaceAsync(pending.Path, token));

        if (!transferResult.Success)
        {
            return Finish(transferResult.Message);
        }

        ImportConflictState.Clear();
        _onReload?.Invoke();
        return Finish(transferResult.Message);
    }

    private CommandResult CancelImportConflict()
    {
        ImportConflictState.Clear();
        RebuildTemplate();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.StayOnSettings("Import cancelled.");
    }

    private CommandResult Finish(string message)
    {
        RebuildTemplate();
        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.StayOnSettings(message);
    }

    private void RebuildTemplate()
    {
        var hasConflict = ImportConflictState.HasPending;
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader("Backup & transfer"),
            SettingsCardJson.SubtleText("Export your shortcuts or import a backup file."),
        };

        var conflictBlock = BuildImportConflictBlock();
        if (!string.IsNullOrWhiteSpace(conflictBlock))
        {
            bodyParts.Add(conflictBlock);
        }

        if (hasConflict)
        {
            bodyParts.Add(SettingsCardJson.SubtleText(
                "Merge keeps every shortcut you already have and adds the file. Names that clash are renamed (for example \"My App Copy\"). " +
                "Replace all deletes all current shortcuts and favorites, then loads only what is in the file."));
            bodyParts.Add(BuildImportConflictActionSet());
        }

        var bodyJson = string.Join(",\n                ", bodyParts);
        var primaryActions = hasConflict ? string.Empty : """
            "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Export",
                  "associatedInputs": "none",
                  "data": { "action": "export" }
                },
                {
                  "type": "Action.Submit",
                  "title": "Import",
                  "associatedInputs": "none",
                  "data": { "action": "import" }
                }
              ]
            """;

        TemplateJson = hasConflict
            ? $$"""
                {
                  "type": "AdaptiveCard",
                  "version": "1.6",
                  "body": [
                    {{bodyJson}}
                  ]
                }
                """
            : $$"""
                {
                  "type": "AdaptiveCard",
                  "version": "1.6",
                  "body": [
                    {{bodyJson}}
                  ],
                  {{primaryActions}}
                }
                """;
    }

    private static string BuildImportConflictBlock()
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(pending.Path);
        var conflictLabel = pending.ConflictCount == 1 ? "name" : "names";
        var importLabel = pending.ImportCount == 1 ? "shortcut" : "shortcuts";
        var summary =
            $"Import paused: {pending.ConflictCount} duplicate {conflictLabel} in {fileName} ({pending.ImportCount} {importLabel}). Choose how to finish.";

        return SettingsCardJson.StatusText(summary, SettingsFeedbackTone.Warning);
    }

    private static string BuildImportConflictActionSet() =>
        """
        {
          "type": "ActionSet",
          "spacing": "Medium",
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Merge (rename duplicates)",
              "tooltip": "Keep your shortcuts and add imported ones. Duplicate names become \"Name Copy\", \"Name Copy 2\", and so on.",
              "associatedInputs": "none",
              "data": { "action": "merge" }
            },
            {
              "type": "Action.Submit",
              "title": "Replace all",
              "tooltip": "Delete every shortcut you have now (including favorites) and replace them with the imported file only.",
              "associatedInputs": "none",
              "data": { "action": "replace" }
            },
            {
              "type": "Action.Submit",
              "title": "Cancel import",
              "tooltip": "Discard this import file and keep your shortcuts unchanged.",
              "associatedInputs": "none",
              "data": { "action": "cancel" }
            }
          ]
        }
        """;

    private static ShortcutTransferResult ExecuteImportAction(Func<CancellationToken, Task<ShortcutTransferResult>> action)
    {
        using var cancellation = new CancellationTokenSource(IoTimeout);
        return action(cancellation.Token).GetAwaiter().GetResult();
    }

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();
}
