using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Pages;

internal sealed partial class HomeDisplaySettingsForm : FormContent
{
    private readonly QuickShellSettingsManager _settingsManager;
    private readonly Action? _onReload;
    private readonly Action? _onSettingsChanged;
    private int _pendingRecentCount;

    public HomeDisplaySettingsForm(
        QuickShellSettingsManager settingsManager,
        Action? onReload = null,
        Action? onSettingsChanged = null)
    {
        _settingsManager = settingsManager;
        _onReload = onReload;
        _onSettingsChanged = onSettingsChanged;
        _pendingRecentCount = settingsManager.RecentWorkspaceCount;
        RebuildTemplate();
    }

    public override CommandResult SubmitForm(string payload) => SubmitForm(payload, string.Empty);

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var action = TryGetAction(data) ?? TryGetActionFromInputs(inputs);
        return action switch
        {
            "recentDecrement" => AdjustRecentCount(-1),
            "recentIncrement" => AdjustRecentCount(1),
            _ => CommandResult.KeepOpen(),
        };
    }

    private CommandResult AdjustRecentCount(int delta)
    {
        var next = QuickShellRecentSettings.NormalizeCount(_pendingRecentCount + delta);
        if (next == _pendingRecentCount)
        {
            return CommandResult.KeepOpen();
        }

        _pendingRecentCount = next;
        RebuildTemplate();
        ScheduleDebouncedCommit();
        return CommandResult.KeepOpen();
    }

    private void ScheduleDebouncedCommit()
    {
        SettingsFormHelpers.ScheduleDebouncedReload(CommitPendingRecentCount);
    }

    private void CommitPendingRecentCount()
    {
        if (_pendingRecentCount == _settingsManager.RecentWorkspaceCount)
        {
            return;
        }

        _settingsManager.UpdateRecentWorkspaceCount(_pendingRecentCount);
        _onReload?.Invoke();
        _onSettingsChanged?.Invoke();
        QuickShellStatus.ShowToast("Saved");
    }

    private void RebuildTemplate()
    {
        var bodyParts = new List<string>
        {
            SettingsCardJson.SectionHeader("Home display"),
            SettingsCardJson.RecentCountStepper(_pendingRecentCount),
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
}
