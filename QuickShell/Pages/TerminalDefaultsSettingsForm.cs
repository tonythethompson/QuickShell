using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class TerminalDefaultsSettingsForm : FormContent
{
    private const string TerminalApplicationField = "terminalApplication";
    private const string DefaultProfileField = "defaultProfile";

    private static readonly HashSet<string> HandledActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "refreshTerminals",
        "previewTerminalDefaults",
    };

    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private readonly Action? _onBodyChanged;
    private string _pendingApp = TerminalHostIds.WindowsTerminal;
    private string _pendingProfile = TerminalHostIds.DefaultProfile;

    /// <summary>Body elements for embedding in the combined settings card.</summary>
    public string BodyElementsJson { get; private set; } = string.Empty;

    /// <summary>Data JSON for terminal application / profile bindings.</summary>
    public string BoundDataJson { get; private set; } = "{}";

    public bool MatchesPending(string appId, string profileId) =>
        string.Equals(_pendingApp, appId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(_pendingProfile, profileId, StringComparison.OrdinalIgnoreCase);

    public TerminalDefaultsSettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null,
        Action? onBodyChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _onBodyChanged = onBodyChanged;
        SyncPendingFromSettings();
        RebuildTemplate(notifyParent: false);
    }

    internal void SyncFromSettings(bool notifyParent = true)
    {
        SyncPendingFromSettings();
        RebuildTemplate(notifyParent);
    }

    public override CommandResult SubmitForm(string inputs, string data) =>
        HandleSubmit(inputs, data);

    public override CommandResult SubmitForm(string payload) =>
        HandleSubmit(payload, string.Empty);

    public bool TryHandleAction(string? action, string inputs, string data, out CommandResult result)
    {
        if (action is null || !HandledActions.Contains(action))
        {
            result = CommandResult.KeepOpen();
            return false;
        }

        result = HandleSubmit(inputs, data);
        return true;
    }

    private CommandResult HandleSubmit(string inputs, string data)
    {
        var values = ParseValues(inputs, data);
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);

        if (action == "refreshTerminals")
        {
            ApplyPendingFromValues(values);
            return RefreshTerminals();
        }

        if (action == "previewTerminalDefaults")
        {
            return PreviewTerminalDefaults(values);
        }

        return CommandResult.KeepOpen();
    }

    /// <summary>Updates pending app/profile from form values without writing settings.</summary>
    public void ApplyPendingFromValues(JsonObject? values)
    {
        var app = SettingsFormValueReader.ReadString(values, TerminalApplicationField) ?? _pendingApp;
        var profile = SettingsFormValueReader.ReadString(values, DefaultProfileField) ?? _pendingProfile;
        _pendingApp = app;
        var profileChoices = TerminalCatalogChoices.GetDefaultProfileChoices(_settingsManager.Services.TerminalCatalog, _pendingApp);
        _pendingProfile = profileChoices.Any(choice =>
            string.Equals(choice.Value, profile, StringComparison.OrdinalIgnoreCase))
            ? profile
            : profileChoices.FirstOrDefault()?.Value ?? TerminalHostIds.DefaultProfile;
        RebuildTemplate(notifyParent: false);
    }

    /// <summary>Writes pending terminal defaults. Returns false when values are invalid.</summary>
    public bool TryCommitPending(JsonObject? values, out string? error)
    {
        ApplyPendingFromValues(values);
        if (string.IsNullOrWhiteSpace(_pendingApp) || string.IsNullOrWhiteSpace(_pendingProfile))
        {
            error = Strings.TerminalDefaults_PickAppAndProfile_Error;
            return false;
        }

        _settingsManager.UpdateTerminalDefaults(_pendingApp, _pendingProfile);
        SyncPendingFromSettings();
        RebuildTemplate(notifyParent: false);
        error = null;
        return true;
    }

    private CommandResult RefreshTerminals()
    {
        TerminalDiscovery.Refresh(_settingsManager);
        TerminalCatalogChoices.InvalidateCache();
        // Keep unsaved form state while reconciling its profile against the
        // refreshed terminal choices.
        ApplyPendingFromValues(values: null);
        RebuildTemplate();
        _settingsManager.Services.RefreshScheduler.SchedulePostNavigationRefresh(_onReload);
        _settingsManager.Services.RefreshScheduler.ScheduleRefresh(_onSettingsChanged);
        return QuickShellNavigation.StayOnSettings(Strings.TerminalDefaults_ListRefreshed_Status);
    }

    private CommandResult PreviewTerminalDefaults(JsonObject? values)
    {
        var app = SettingsFormValueReader.ReadString(values, TerminalApplicationField) ?? _pendingApp;
        var profile = SettingsFormValueReader.ReadString(values, DefaultProfileField) ?? _pendingProfile;

        _pendingApp = app;
        var profileChoices = TerminalCatalogChoices.GetDefaultProfileChoices(_settingsManager.Services.TerminalCatalog, _pendingApp);
        _pendingProfile = profileChoices.Any(choice =>
            string.Equals(choice.Value, profile, StringComparison.OrdinalIgnoreCase))
            ? profile
            : profileChoices.FirstOrDefault()?.Value ?? TerminalHostIds.DefaultProfile;

        RebuildTemplate();
        return CommandResult.KeepOpen();
    }

    private void SyncPendingFromSettings()
    {
        _pendingApp = _settingsManager.TerminalApplicationId;
        _pendingProfile = _settingsManager.DefaultProfileId;
    }

    private void RebuildTemplate() => RebuildTemplate(notifyParent: true);

    private void RebuildTemplate(bool notifyParent)
    {
        var appChoices = TerminalCatalogChoices.GetTerminalApplicationChoicesJson(_settingsManager.Services.TerminalCatalog);
        var profileChoices = TerminalCatalogChoices.GetDefaultProfileChoicesJson(_settingsManager.Services.TerminalCatalog, _pendingApp);
        // Compact symbol only — MDL2 E72C is private-use and shows as □ in CmdPal action titles.
        // Save lives on the page footer as Save & close.
        var refreshAction = AdaptiveCardFormJson.IconSubmitAction(
            FormActionGlyphs.RefreshActionTitle,
            FormActionGlyphs.RefreshProfileListTooltip,
            "refreshTerminals",
            "auto");

        var bodyParts = new List<string>
        {
            $$"""
            {
              "type": "TextBlock",
              "text": "{{EscapeJson(Strings.TerminalDefaults_SectionHeader)}}",
              "weight": "Bolder",
              "size": "Medium",
              "spacing": "None",
              "tooltip": "{{EscapeJson(FormActionGlyphs.TerminalDefaultsSectionTooltip)}}"
            }
            """,
            $$"""
            {
              "type": "ColumnSet",
              "spacing": "Small",
              "columns": [
                {
                  "type": "Column",
                  "width": "1",
                  "items": [
                    {
                      "type": "Input.ChoiceSet",
                      "id": "{{TerminalApplicationField}}",
                      "label": "{{EscapeJson(Strings.TerminalDefaults_AppField_Label)}}",
                      "style": "compact",
                      "spacing": "None",
                      "value": "${terminalApplication}",
                      "changeAction": {
                        "type": "Action.Submit",
                        "associatedInputs": "auto",
                        "data": { "action": "previewTerminalDefaults" }
                      },
                      "choices": [
                        {{appChoices}}
                      ]
                    }
                  ]
                },
                {
                  "type": "Column",
                  "width": "1",
                  "items": [
                    {
                      "type": "Input.ChoiceSet",
                      "id": "{{DefaultProfileField}}",
                      "label": "{{EscapeJson(Strings.TerminalDefaults_ProfileField_Label)}}",
                      "style": "compact",
                      "spacing": "None",
                      "value": "${defaultProfile}",
                      "choices": [
                        {{profileChoices}}
                      ]
                    }
                  ]
                },
                {{AdaptiveCardFormJson.ActionColumn(refreshAction, "Bottom")}}
              ]
            }
            """,
        };

        var bodyJson = string.Join(",\n                ", bodyParts);
        BodyElementsJson = bodyJson;
        BoundDataJson = $$"""
            {
              "terminalApplication": "{{EscapeJson(_pendingApp)}}",
              "defaultProfile": "{{EscapeJson(_pendingProfile)}}"
            }
            """;

        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {{bodyJson}}
              ]
            }
            """;
        DataJson = BoundDataJson;

        if (notifyParent)
        {
            _onBodyChanged?.Invoke();
        }
    }

    private static string? TryGetAction(string? data) =>
        string.IsNullOrWhiteSpace(data)
            ? null
            : JsonNode.Parse(data)?.AsObject()?["action"]?.GetValue<string>();

    private static string? TryGetActionFromInputs(string inputs) =>
        string.IsNullOrWhiteSpace(inputs)
            ? null
            : JsonNode.Parse(inputs)?.AsObject()?["action"]?.GetValue<string>();

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
                    if (property.Key.Equals("action", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    merged[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        return merged;
    }

    private static string EscapeJson(string value)
    {
        var serialized = JsonSerializer.Serialize(value, QuickShellJsonContext.Default.String);
        return serialized.Substring(1, serialized.Length - 2);
    }
}
