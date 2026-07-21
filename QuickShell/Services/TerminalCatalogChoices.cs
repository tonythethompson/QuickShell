using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell;

internal static class TerminalCatalogChoices
{
    private static readonly object CacheLock = new();
    private static ITerminalCatalog? _appChoicesCatalog;
    private static List<ChoiceSetSetting.Choice>? _appChoices;
    private static string? _appChoicesJson;
    private static ITerminalCatalog? _profileChoicesCatalog;
    private static string? _profileChoicesAppId;
    private static List<ChoiceSetSetting.Choice>? _profileChoices;
    private static string? _profileChoicesJson;

    public static List<ChoiceSetSetting.Choice> GetTerminalApplicationChoices(ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        lock (CacheLock)
        {
            if (_appChoices is not null && ReferenceEquals(_appChoicesCatalog, catalog))
            {
                return _appChoices;
            }
        }

        var choices = BuildAppChoices(catalog);
        lock (CacheLock)
        {
            _appChoicesCatalog = catalog;
            _appChoices = choices;
            _appChoicesJson = SettingsCardJson.BuildChoicesJson(choices);
            return _appChoices;
        }
    }

    /// <summary>Prebuilt Adaptive Card choices JSON for the settings terminal app dropdown.</summary>
    public static string GetTerminalApplicationChoicesJson(ITerminalCatalog catalog)
    {
        _ = GetTerminalApplicationChoices(catalog);
        lock (CacheLock)
        {
            return _appChoicesJson ?? "[]";
        }
    }

    public static List<ChoiceSetSetting.Choice> GetMinimalDefaultProfileChoices() =>
    [
        new("Default profile for this app", TerminalHostIds.DefaultProfile),
    ];

    public static List<ChoiceSetSetting.Choice> GetDefaultProfileChoices(ITerminalCatalog catalog, string terminalApplicationId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var appId = terminalApplicationId ?? string.Empty;
        lock (CacheLock)
        {
            if (_profileChoices is not null
                && ReferenceEquals(_profileChoicesCatalog, catalog)
                && string.Equals(_profileChoicesAppId, appId, StringComparison.OrdinalIgnoreCase))
            {
                return _profileChoices;
            }
        }

        var choices = BuildProfileChoices(catalog, appId);
        lock (CacheLock)
        {
            _profileChoicesCatalog = catalog;
            _profileChoicesAppId = appId;
            _profileChoices = choices;
            _profileChoicesJson = SettingsCardJson.BuildChoicesJson(choices);
            return _profileChoices;
        }
    }

    public static string GetDefaultProfileChoicesJson(ITerminalCatalog catalog, string terminalApplicationId)
    {
        _ = GetDefaultProfileChoices(catalog, terminalApplicationId);
        lock (CacheLock)
        {
            if (ReferenceEquals(_profileChoicesCatalog, catalog)
                && string.Equals(_profileChoicesAppId, terminalApplicationId, StringComparison.OrdinalIgnoreCase)
                && _profileChoicesJson is not null)
            {
                return _profileChoicesJson;
            }
        }

        return SettingsCardJson.BuildChoicesJson(GetDefaultProfileChoices(catalog, terminalApplicationId));
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            _appChoicesCatalog = null;
            _appChoices = null;
            _appChoicesJson = null;
            _profileChoicesCatalog = null;
            _profileChoicesAppId = null;
            _profileChoices = null;
            _profileChoicesJson = null;
        }
    }

    public static List<ChoiceSetSetting.Choice> GetSettingsChoices(ITerminalCatalog catalog) =>
        GetTerminalApplicationChoices(catalog);

    private static List<ChoiceSetSetting.Choice> BuildAppChoices(ITerminalCatalog catalog)
    {
        var choices = new List<ChoiceSetSetting.Choice>
        {
            new("Let Windows choose", TerminalHostIds.LetWindowsChoose),
            new("Windows Terminal", TerminalHostIds.WindowsTerminal),
            new("Windows Console Host", TerminalHostIds.WindowsConsoleHost),
        };

        if (catalog.HasTerminalApplication(TerminalHostIds.IntelligentTerminal))
        {
            choices.Add(new ChoiceSetSetting.Choice("Intelligent Terminal", TerminalHostIds.IntelligentTerminal));
        }

        return choices;
    }

    private static List<ChoiceSetSetting.Choice> BuildProfileChoices(ITerminalCatalog catalog, string terminalApplicationId) =>
        catalog.GetDefaultProfileIds(terminalApplicationId)
            .Select(id => id.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase)
                ? new ChoiceSetSetting.Choice("Default profile for this app", id)
                : id switch
                {
                    "powershell" => new ChoiceSetSetting.Choice("PowerShell", id),
                    "pwsh" => new ChoiceSetSetting.Choice("PowerShell 7", id),
                    "cmd" => new ChoiceSetSetting.Choice("Command Prompt", id),
                    _ => new ChoiceSetSetting.Choice(id, id),
                })
            .ToList();
}
