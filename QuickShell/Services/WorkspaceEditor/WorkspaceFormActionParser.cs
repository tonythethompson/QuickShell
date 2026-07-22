using System.Text.Json.Nodes;

namespace QuickShell.Services.WorkspaceEditor;

internal static class WorkspaceFormActionParser
{
    public static WorkspaceFormAction Parse(string inputs, string? data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (string.IsNullOrWhiteSpace(action))
        {
            // Primary "Save workspace" submit has no action payload; preserve default-submit.
            return new WorkspaceFormAction(WorkspaceFormActionKind.Save);
        }

        var source = data ?? inputs;
        return action.ToLowerInvariant() switch
        {
            "save" => new WorkspaceFormAction(WorkspaceFormActionKind.Save),
            "discard" => new WorkspaceFormAction(WorkspaceFormActionKind.Discard),
            "cancel" => new WorkspaceFormAction(WorkspaceFormActionKind.Cancel),
            "browse" => new WorkspaceFormAction(WorkspaceFormActionKind.Browse),
            "paste" => new WorkspaceFormAction(WorkspaceFormActionKind.Paste),
            "refreshterminals" => new WorkspaceFormAction(WorkspaceFormActionKind.RefreshTerminals),
            "addsuggestedcommand" => ParseAddSuggestedCommand(source),
            "addcommandrow" => new WorkspaceFormAction(WorkspaceFormActionKind.AddCommandRow),
            "addopeninterminalrow" => new WorkspaceFormAction(WorkspaceFormActionKind.AddOpenInTerminalRow),
            "removelaunch" => ParseRemoveLaunch(source),
            "expandsuggestionpills" => new WorkspaceFormAction(WorkspaceFormActionKind.ExpandSuggestionPills),
            "collapsesuggestionpills" => new WorkspaceFormAction(WorkspaceFormActionKind.CollapseSuggestionPills),
            "addcompanionapp" => new WorkspaceFormAction(WorkspaceFormActionKind.AddCompanionApp),
            "removecompanionapp" => ParseRemoveCompanionApp(source),
            "browsecompanionapp" => ParseBrowseCompanionApp(source),
            "applycompanionpreset" => ParseApplyCompanionPreset(source),
            "help" => new WorkspaceFormAction(WorkspaceFormActionKind.Help),
            _ => new WorkspaceFormAction(WorkspaceFormActionKind.None),
        };
    }

    public static WorkspaceFormAction ParseDiscardPromptAction(string inputs, string? data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        if (string.IsNullOrWhiteSpace(action))
        {
            return new WorkspaceFormAction(WorkspaceFormActionKind.None);
        }

        return action.ToLowerInvariant() switch
        {
            "save" => new WorkspaceFormAction(WorkspaceFormActionKind.Save),
            "discard" => new WorkspaceFormAction(WorkspaceFormActionKind.Discard),
            _ => new WorkspaceFormAction(WorkspaceFormActionKind.None),
        };
    }

    private static WorkspaceFormAction ParseAddSuggestedCommand(string source)
    {
        var node = JsonNode.Parse(source)?.AsObject();
        if (node is null)
        {
            return new WorkspaceFormAction(WorkspaceFormActionKind.None);
        }

        var pillCommand = node["pillCommand"]?.ToString();
        var pillTaskType = node["pillTaskType"]?.ToString();
        var pillIndex = -1;
        if (node["pillIndex"] is not null)
        {
            _ = int.TryParse(node["pillIndex"]?.ToString(), out pillIndex);
        }

        return new WorkspaceFormAction(
            WorkspaceFormActionKind.AddSuggestedCommand,
            PillCommand: pillCommand,
            PillTaskType: pillTaskType,
            PillIndex: pillIndex);
    }

    private static WorkspaceFormAction ParseRemoveLaunch(string source)
    {
        var node = JsonNode.Parse(source)?.AsObject();
        if (node is null || !TryReadInt(node, "launchIndex", out var index))
        {
            return new WorkspaceFormAction(WorkspaceFormActionKind.None);
        }

        return new WorkspaceFormAction(WorkspaceFormActionKind.RemoveLaunch, LaunchIndex: index);
    }

    private static WorkspaceFormAction ParseRemoveCompanionApp(string source)
    {
        var node = JsonNode.Parse(source)?.AsObject();
        if (node is null || !TryReadInt(node, "companionIndex", out var index))
        {
            return new WorkspaceFormAction(WorkspaceFormActionKind.None);
        }

        return new WorkspaceFormAction(WorkspaceFormActionKind.RemoveCompanionApp, CompanionIndex: index);
    }

    private static WorkspaceFormAction ParseBrowseCompanionApp(string source)
    {
        var node = JsonNode.Parse(source)?.AsObject();
        if (node is null || !TryReadInt(node, "companionIndex", out var index))
        {
            return new WorkspaceFormAction(WorkspaceFormActionKind.None);
        }

        return new WorkspaceFormAction(WorkspaceFormActionKind.BrowseCompanionApp, CompanionIndex: index);
    }

    private static WorkspaceFormAction ParseApplyCompanionPreset(string source)
    {
        var node = JsonNode.Parse(source)?.AsObject();
        if (node is null)
        {
            return new WorkspaceFormAction(WorkspaceFormActionKind.None);
        }

        var index = 0;
        if (node["companionIndex"] is not null)
        {
            _ = int.TryParse(node["companionIndex"]?.ToString(), out index);
        }

        var preset = node["preset"]?.ToString() ?? string.Empty;
        return new WorkspaceFormAction(WorkspaceFormActionKind.ApplyCompanionPreset, CompanionIndex: index, Preset: preset);
    }

    private static bool TryReadInt(JsonObject node, string key, out int value)
    {
        value = 0;
        if (node[key] is null)
        {
            return false;
        }

        return int.TryParse(node[key]?.ToString(), out value);
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
}
