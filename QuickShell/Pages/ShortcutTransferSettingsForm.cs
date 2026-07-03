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
            "exportWorkspaces" => RunWorkspaceExport(),
            "importWorkspaces" => RunWorkspaceImport(),
            "resetWorkspaces" => ConfirmResetWorkspaces(),
            "merge" => ResolveImportConflict(merge: true),
            "replace" => ResolveImportConflict(merge: false),
            "cancel" => CancelImportConflict(),
            _ => CommandResult.KeepOpen(),
        };

        return result;
    }

    private CommandResult RunWorkspaceExport()
    {
        var result = new ExportShortcutsCommand(stayOnSettings: true).Invoke();
        RebuildTemplate();
        return result;
    }

    private CommandResult RunWorkspaceImport()
    {
        var result = new ImportShortcutsCommand(
            _onReload ?? (() => { }),
            stayOnSettings: true,
            onSettingsRefresh: _onSettingsChanged).Invoke();
        RebuildTemplate();
        return result;
    }

    private CommandResult ConfirmResetWorkspaces()
    {
        var count = QuickShellRuntimeServices.Shortcuts.GetShortcuts().Count;
        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = "Reset all workspaces?",
            Description = BuildResetDescription("workspace", count, QuickShellRuntimeServices.Shortcuts.ConfigPath),
            PrimaryCommand = new ResetProjectsCommand(_onReload ?? (() => { }), _onSettingsChanged),
        });
    }

    private static string BuildResetDescription(string itemLabel, int count, string configPath)
    {
        var itemsLabel = count == 1 ? itemLabel : $"{itemLabel}s";
        var countLine = count == 0
            ? $"You have no saved {itemsLabel}."
            : $"This deletes {count} saved {itemsLabel}.";

        var backupName = Path.GetFileName(configPath) + ".bak";
        return $"{countLine} A backup from your last save remains as {backupName} in the same folder.";
    }

    private CommandResult ResolveImportConflict(bool merge)
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return Finish("No import is pending.");
        }

        var transferResult = pending.Kind switch
        {
            ImportTransferKind.Projects => ExecuteProjectImportAction(token => merge
                ? QuickShellRuntimeServices.Shortcuts.ImportMergeAsync(pending.Path, token)
                : QuickShellRuntimeServices.Shortcuts.ImportReplaceAsync(pending.Path, token)),
            _ => new ImportTransferResult(false, "Unknown import type."),
        };

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
        };

        if (!hasConflict)
        {
            bodyParts.Add(SettingsCardJson.TransferRow(
                "Workspaces",
                "Export, import, or reset saved workspace folders and favorites.",
                BuildWorkspaceTransferActionSet(),
                topSpacing: "Medium"));
        }

        var conflictBlock = BuildImportConflictBlock();
        if (!string.IsNullOrWhiteSpace(conflictBlock))
        {
            bodyParts.Add(conflictBlock);
        }

        if (hasConflict)
        {
            bodyParts.Add(SettingsCardJson.SubtleText(BuildImportConflictHelpText()));
            bodyParts.Add(BuildImportConflictActionSet());
        }

        var bodyJson = string.Join(",\n                ", bodyParts);

        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {{bodyJson}}
              ]
            }
            """;
    }

    private static string BuildWorkspaceTransferActionSet() =>
        SettingsCardJson.TransferActionRow(
            """
            {
              "type": "Action.Submit",
              "title": "Export",
              "associatedInputs": "none",
              "data": { "action": "exportWorkspaces" }
            }
            """,
            """
            {
              "type": "Action.Submit",
              "title": "Import",
              "associatedInputs": "none",
              "data": { "action": "importWorkspaces" }
            }
            """,
            """
            {
              "type": "Action.Submit",
              "title": "Reset",
              "style": "destructive",
              "tooltip": "Delete every workspace you have saved. Use Undo (Ctrl+Z) to restore.",
              "associatedInputs": "none",
              "data": { "action": "resetWorkspaces" }
            }
            """);

    private static string BuildImportConflictBlock()
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(pending.Path);
        var itemsLabel = pending.ImportCount == 1 ? "workspace" : "workspaces";
        var conflictLabel = pending.ConflictCount == 1 ? "name" : "names";
        var summary =
            $"Import paused: {pending.ConflictCount} duplicate {conflictLabel} in {fileName} ({pending.ImportCount} {itemsLabel}). Choose how to finish.";

        return SettingsCardJson.StatusText(summary, SettingsFeedbackTone.Warning);
    }

    private static string BuildImportConflictHelpText() =>
        "Merge keeps every workspace you already have and adds the file. Names that clash are renamed (for example \"My App (2)\"). " +
        "Replace all deletes all current workspaces and favorites, then loads only what is in the file.";

    private static string BuildImportConflictActionSet() => """
        {
          "type": "ActionSet",
          "spacing": "Medium",
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Merge (rename duplicates)",
              "associatedInputs": "none",
              "data": { "action": "merge" }
            },
            {
              "type": "Action.Submit",
              "title": "Replace all workspaces",
              "associatedInputs": "none",
              "data": { "action": "replace" }
            },
            {
              "type": "Action.Submit",
              "title": "Cancel import",
              "associatedInputs": "none",
              "data": { "action": "cancel" }
            }
          ]
        }
        """;

    private static ImportTransferResult ExecuteProjectImportAction(
        Func<CancellationToken, Task<ShortcutTransferResult>> action)
    {
        using var cancellation = new CancellationTokenSource(IoTimeout);
        var result = action(cancellation.Token).GetAwaiter().GetResult();
        return new ImportTransferResult(result.Success, result.Message);
    }

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();

    private readonly record struct ImportTransferResult(bool Success, string Message);
}
