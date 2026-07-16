using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Pages;

internal sealed partial class ShortcutTransferSettingsForm : FormContent
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> HandledActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "exportWorkspaces",
        "importWorkspaces",
        "resetWorkspaces",
        "copyLaunchDiagnostics",
        "copySupportBundle",
        "openSupportLogs",
        "merge",
        "replace",
        "cancel",
    };

    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private readonly Action? _onBodyChanged;

    /// <summary>Body elements for embedding under Home / Multi / Git in one Adaptive Card.</summary>
    public string BodyElementsJson { get; private set; } = "[]";

    public ShortcutTransferSettingsForm(
        Action? onReload,
        Action? onSettingsChanged = null,
        Action? onBodyChanged = null)
    {
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _onBodyChanged = onBodyChanged;
        RebuildTemplate(notifyParent: false);
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return TryHandleAction(action, inputs, data, out var result)
            ? result
            : CommandResult.KeepOpen();
    }

    /// <summary>Handles backup/import/diagnostics actions when this form is embedded.</summary>
    public bool TryHandleAction(string? action, string inputs, string data, out CommandResult result)
    {
        if (action is null || !HandledActions.Contains(action))
        {
            result = CommandResult.KeepOpen();
            return false;
        }

        result = action switch
        {
            "exportWorkspaces" => RunWorkspaceExport(),
            "importWorkspaces" => RunWorkspaceImport(),
            "resetWorkspaces" => ConfirmResetWorkspaces(),
            "copyLaunchDiagnostics" => CopyLaunchDiagnostics(),
            "copySupportBundle" => CopySupportBundle(),
            "openSupportLogs" => OpenSupportLogs(),
            "merge" => ResolveImportConflict(merge: true),
            "replace" => ResolveImportConflict(merge: false),
            "cancel" => CancelImportConflict(),
            _ => CommandResult.KeepOpen(),
        };
        return true;
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
        var count = QuickShellServices.Current.Shortcuts.GetShortcuts().Count;
        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = Strings.ResetProjects_Title,
            Description = BuildResetDescription(count, QuickShellServices.Current.Shortcuts.ConfigPath),
            PrimaryCommand = new ResetProjectsCommand(_onReload ?? (() => { }), _onSettingsChanged),
        });
    }

    private static string BuildResetDescription(int count, string configPath)
    {
        var itemLabel = Strings.Word_Workspace_Singular;
        var itemsLabel = count == 1 ? itemLabel : Strings.Word_Workspace_Plural;
        var countLine = count == 0
            ? Strings.ResetWorkspaces_NoneSavedFormat(itemsLabel)
            : Strings.ResetWorkspaces_DeleteCountFormat(count, itemsLabel);

        var backupName = Path.GetFileName(configPath) + ".bak";
        return Strings.ResetWorkspaces_BackupNoteFormat(countLine, backupName);
    }

    private CommandResult ResolveImportConflict(bool merge)
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return Finish(Strings.ImportConflictPage_NoImportPending);
        }

        var transferResult = pending.Kind switch
        {
            ImportTransferKind.Projects => ExecuteProjectImportAction(token => merge
                ? QuickShellServices.Current.Shortcuts.ImportMergeAsync(pending.Path, token)
                : QuickShellServices.Current.Shortcuts.ImportReplaceAsync(pending.Path, token)),
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

    private void RebuildTemplate() => RebuildTemplate(notifyParent: true);

    private void RebuildTemplate(bool notifyParent)
    {
        var hasConflict = ImportConflictState.HasPending;
        // Medium gap under Home / Multi / Git; another Medium before diagnostics.
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader(
                Strings.ShortcutTransfer_SectionHeader,
                spacing: "Medium",
                tooltip: Strings.ShortcutTransfer_WorkspacesRow_Description),
        };

        if (!hasConflict)
        {
            bodyParts.Add(SettingsCardJson.TransferActionsBlock(
                BuildWorkspaceTransferActionSet(),
                topSpacing: "Small"));

            bodyParts.Add(SettingsCardJson.TransferActionsBlock(
                BuildLaunchDiagnosticsActionSet(),
                topSpacing: "Medium",
                header: Strings.Diagnostics_SectionHeader,
                tooltip: Strings.Diagnostics_CopyLaunch_Tooltip));
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
        BodyElementsJson = bodyJson;

        // Standalone card (not used when embedded under BehaviorSettingsForm).
        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {{bodyJson}}
              ]
            }
            """;

        if (notifyParent)
        {
            _onBodyChanged?.Invoke();
        }
    }

    private static string BuildWorkspaceTransferActionSet() =>
        SettingsCardJson.TransferActionRow(
            $$"""
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ShortcutTransfer_ExportButton_Title)}}",
              "associatedInputs": "none",
              "data": { "action": "exportWorkspaces" }
            }
            """,
            $$"""
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ShortcutTransfer_ImportButton_Title)}}",
              "associatedInputs": "none",
              "data": { "action": "importWorkspaces" }
            }
            """,
            $$"""
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ShortcutTransfer_ResetButton_Title)}}",
              "style": "destructive",
              "tooltip": "{{Escape(Strings.ShortcutTransfer_ResetButton_Tooltip)}}",
              "associatedInputs": "none",
              "data": { "action": "resetWorkspaces" }
            }
            """);

    private static string BuildLaunchDiagnosticsActionSet() => $$"""
        {
          "type": "ActionSet",
          "spacing": "None",
          "actions": [
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.Diagnostics_CopyLaunch_Title)}}",
              "tooltip": "{{Escape(Strings.Diagnostics_CopyLaunch_Tooltip)}}",
              "associatedInputs": "none",
              "data": { "action": "copyLaunchDiagnostics" }
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.Diagnostics_CopySupportBundle_Title)}}",
              "tooltip": "{{Escape(Strings.Diagnostics_CopySupportBundle_Tooltip)}}",
              "associatedInputs": "none",
              "data": { "action": "copySupportBundle" }
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.Diagnostics_OpenLogFolder_Title)}}",
              "tooltip": "{{Escape(Strings.Diagnostics_OpenLogFolder_Tooltip)}}",
              "associatedInputs": "none",
              "data": { "action": "openSupportLogs" }
            }
          ]
        }
        """;

    private static CommandResult CopyLaunchDiagnostics()
    {
        LaunchDiagnosticsState.TryCopyLastReport(out var message);
        return QuickShellNavigation.StayOnSettings(message);
    }

    private static CommandResult CopySupportBundle()
    {
        SupportDiagnostics.TryCopyBundle(LaunchDiagnosticsState.LastReport, out var message);
        return QuickShellNavigation.StayOnSettings(message);
    }

    private static CommandResult OpenSupportLogs()
    {
        return SupportDiagnostics.TryOpenLogFolder(out var error)
            ? QuickShellNavigation.StayOnSettings(Strings.Diagnostics_LogFolderOpened)
            : QuickShellNavigation.StayOnSettings(error);
    }

    private static string BuildImportConflictBlock()
    {
        var pending = ImportConflictState.Pending;
        if (pending is null)
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(pending.Path);
        var itemsLabel = pending.ImportCount == 1 ? Strings.Word_Workspace_Singular : Strings.Word_Workspace_Plural;
        var conflictLabel = pending.ConflictCount == 1 ? Strings.Word_Name_Singular : Strings.Word_Name_Plural;
        var summary = Strings.ImportConflict_Summary_WarningFormat(
            pending.ConflictCount, conflictLabel, fileName, pending.ImportCount, itemsLabel);

        return SettingsCardJson.StatusText(summary, SettingsFeedbackTone.Warning);
    }

    private static string BuildImportConflictHelpText() => Strings.ImportConflict_HelpText;

    private static string BuildImportConflictActionSet() => $$"""
        {
          "type": "ActionSet",
          "spacing": "Small",
          "actions": [
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ImportConflict_MergeButton_Title)}}",
              "associatedInputs": "none",
              "data": { "action": "merge" }
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ImportConflict_ReplaceButton_Title)}}",
              "associatedInputs": "none",
              "data": { "action": "replace" }
            },
            {
              "type": "Action.Submit",
              "title": "{{Escape(Strings.ImportConflict_CancelButton_Title)}}",
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

    private static string Escape(string value)
    {
        var serialized = JsonSerializer.Serialize(value, QuickShellJsonContext.Default.String);
        return serialized.Substring(1, serialized.Length - 2);
    }

    private readonly record struct ImportTransferResult(bool Success, string Message);
}
