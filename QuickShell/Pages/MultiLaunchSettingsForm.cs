using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class MultiLaunchSettingsForm : FormContent
{
    private const string SingleWindowTabsField = "singleWindowTabs";

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private bool _pendingSingleWindowTabs;

    public MultiLaunchSettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _pendingSingleWindowTabs = !settingsManager.SeparateWindowsForMultiLaunch;
        RebuildTemplate();
    }

    internal void SyncFromSettings()
    {
        _pendingSingleWindowTabs = !_settingsManager.SeparateWindowsForMultiLaunch;
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return action switch
        {
            "saveMultiLaunch" => SaveFromInputs(inputs, data),
            _ => CommandResult.KeepOpen(),
        };
    }

    private CommandResult SaveFromInputs(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        var singleWindowTabs = ParseToggleBool(
            values?[SingleWindowTabsField]?.ToString(),
            _pendingSingleWindowTabs);

        if (singleWindowTabs != !_settingsManager.SeparateWindowsForMultiLaunch)
        {
            _settingsManager.UpdateMultiLaunchPresentation(singleWindowTabs);
            SettingsFormHelpers.SchedulePostNavigationRefresh(_settingsManager.Services.CallbackQueue, _onReload);
            SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);
            QuickShellStatus.ShowToast(Strings.Saved_Toast);
        }

        _pendingSingleWindowTabs = singleWindowTabs;
        RebuildTemplate();
        return CommandResult.KeepOpen();
    }

    private void RebuildTemplate()
    {
        var usesWt = TerminalHostIds.UsesWindowsTerminalProfiles(_settingsManager.TerminalApplicationId);
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader(Strings.MultiLaunch_SectionHeader),
            SettingsCardJson.MultiLaunchTabsToggle(_pendingSingleWindowTabs, usesWt),
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
