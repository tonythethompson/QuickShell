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



    private readonly QuickShellSettingsManager _settingsManager;

    private readonly Action? _onReload;

    private readonly Action? _onSettingsChanged;

    private string _pendingApp = TerminalHostIds.WindowsTerminal;

    private string _pendingProfile = TerminalHostIds.DefaultProfile;



    public TerminalDefaultsSettingsForm(

        QuickShellSettingsManager settingsManager,

        Action? onReload = null,

        Action? onSettingsChanged = null)

    {

        _settingsManager = settingsManager;

        _onReload = onReload;

        _onSettingsChanged = onSettingsChanged;

        SyncPendingFromSettings();

        RebuildTemplate();

    }



    internal void SyncFromSettings()

    {

        SyncPendingFromSettings();

        RebuildTemplate();

    }



    public override CommandResult SubmitForm(string inputs, string data) =>

        HandleSubmit(inputs, data);



    public override CommandResult SubmitForm(string payload) =>

        HandleSubmit(payload, string.Empty);



    private CommandResult HandleSubmit(string inputs, string data)

    {

        var values = ParseValues(inputs, data);

        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);



        if (action == "refreshTerminals")

        {

            return RefreshTerminals();

        }



        if (action == "previewTerminalDefaults")

        {

            return PreviewTerminalDefaults(values);

        }



        if (IsSaveRequest(action))

        {

            return SaveFromValues(values);

        }



        return CommandResult.KeepOpen();

    }



    private CommandResult SaveFromValues(JsonObject? values)

    {

        var previousApp = _pendingApp;

        var previousProfile = _pendingProfile;



        var app = SettingsFormValueReader.ReadString(values, TerminalApplicationField) ?? _pendingApp;

        var profile = SettingsFormValueReader.ReadString(values, DefaultProfileField) ?? _pendingProfile;



        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(profile))

        {

            return QuickShellNavigation.StayOnSettings(Strings.TerminalDefaults_PickAppAndProfile_Error);

        }



        _settingsManager.UpdateTerminalDefaults(app, profile);

        SyncPendingFromSettings();

        RebuildTemplate();

        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);



        if (!previousApp.Equals(_pendingApp, StringComparison.OrdinalIgnoreCase)

            || !previousProfile.Equals(_pendingProfile, StringComparison.OrdinalIgnoreCase))

        {

            QuickShellStatus.ShowToast(Strings.Saved_Toast);

        }



        return CommandResult.KeepOpen();

    }



    private CommandResult RefreshTerminals()

    {

        TerminalDiscovery.Refresh(_settingsManager);

        SyncPendingFromSettings();

        RebuildTemplate();

        SettingsFormHelpers.SchedulePostNavigationRefresh(_onReload);

        SettingsFormHelpers.ScheduleRefresh(_onSettingsChanged);

        return QuickShellNavigation.StayOnSettings(Strings.TerminalDefaults_ListRefreshed_Status);

    }



    private CommandResult PreviewTerminalDefaults(JsonObject? values)

    {

        var app = SettingsFormValueReader.ReadString(values, TerminalApplicationField) ?? _pendingApp;

        var profile = SettingsFormValueReader.ReadString(values, DefaultProfileField) ?? _pendingProfile;



        _pendingApp = app;

        var profileChoices = TerminalCatalogChoices.GetDefaultProfileChoices(_pendingApp);

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



    private void RebuildTemplate()

    {

        var appChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalogChoices.GetTerminalApplicationChoices());

        var profileChoices = SettingsCardJson.BuildChoicesJson(TerminalCatalogChoices.GetDefaultProfileChoices(_pendingApp));

        var refreshAction = AdaptiveCardFormJson.IconSubmitAction(

            FormActionGlyphs.RefreshLabel,

            FormActionGlyphs.RefreshProfileListTooltip,

            "refreshTerminals",

            "none");

        var saveAction = AdaptiveCardFormJson.IconSubmitAction(

            FormActionGlyphs.SaveLabel,

            FormActionGlyphs.SaveTerminalDefaultsTooltip,

            "saveTerminalDefaults");



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

              "type": "TextBlock",

              "text": "{{EscapeJson(Strings.TerminalDefaults_SubtleText)}}",

              "wrap": true,

              "isSubtle": true,

              "spacing": "Small"

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

                {{AdaptiveCardFormJson.ActionColumn(refreshAction, "Bottom")}},

                {{AdaptiveCardFormJson.ActionColumn(saveAction, "Bottom")}}

              ]

            }

            """,

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



        DataJson = $$"""

            {

              "terminalApplication": "{{EscapeJson(_pendingApp)}}",

              "defaultProfile": "{{EscapeJson(_pendingProfile)}}"

            }

            """;

    }



    private static bool IsSaveRequest(string? action) =>

        action == "saveTerminalDefaults";



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

        var serialized = JsonSerializer.Serialize(value);

        return serialized.Substring(1, serialized.Length - 2);

    }

}
