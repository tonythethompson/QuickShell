using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class HomeDisplaySettingsForm : FormContent
{
    private const string ShowRecentsField = "showRecents";

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private bool _pendingShowRecents;

    public HomeDisplaySettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _pendingShowRecents = QuickShellRecentSettings.IsEnabled(settingsManager.RecentWorkspaceCount);
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return action switch
        {
            "saveRecents" => SaveFromInputs(inputs, data),
            _ => CommandResult.KeepOpen(),
        };
    }

    private CommandResult SaveFromInputs(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        var showRecents = ParseToggleBool(values?[ShowRecentsField]?.ToString(), _pendingShowRecents);
        var nextCount = QuickShellRecentSettings.FromEnabled(showRecents);

        if (nextCount != _settingsManager.RecentWorkspaceCount)
        {
            _settingsManager.UpdateRecentWorkspaceCount(nextCount);
            _onReload?.Invoke();
            _onSettingsChanged?.Invoke();
            QuickShellStatus.ShowToast("Saved");
        }

        _pendingShowRecents = showRecents;
        RebuildTemplate();
        return CommandResult.KeepOpen();
    }

    private void RebuildTemplate()
    {
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader("Home display"),
            SettingsCardJson.RecentEnabledToggle(_pendingShowRecents),
        };

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

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.ToString();

    private static string? TryGetActionFromInputs(string inputs) =>
        JsonNode.Parse(inputs)?.AsObject()?["action"]?.ToString();

    private static JsonObject? ParseValues(string inputs, string data)
    {
        JsonObject? merged = null;

        if (!string.IsNullOrWhiteSpace(inputs))
        {
            merged = JsonNode.Parse(inputs)?.AsObject();
        }

        if (!string.IsNullOrWhiteSpace(data))
        {
            var dataObject = JsonNode.Parse(data)?.AsObject();
            if (dataObject is not null)
            {
                merged ??= new JsonObject();
                foreach (var property in dataObject)
                {
                    merged[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        return merged;
    }

    private static bool ParseToggleBool(string? value, bool fallback) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };
}
