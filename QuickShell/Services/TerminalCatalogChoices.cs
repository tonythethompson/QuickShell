using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell;

internal static class TerminalCatalogChoices
{
    public static List<ChoiceSetSetting.Choice> GetTerminalApplicationChoices()
    {
        var choices = new List<ChoiceSetSetting.Choice>
        {
            new("Let Windows choose", TerminalHostIds.LetWindowsChoose),
            new("Windows Terminal", TerminalHostIds.WindowsTerminal),
            new("Windows Console Host", TerminalHostIds.WindowsConsoleHost),
        };

        if (TerminalCatalog.HasTerminalApplication(TerminalHostIds.IntelligentTerminal))
        {
            choices.Add(new ChoiceSetSetting.Choice("Intelligent Terminal", TerminalHostIds.IntelligentTerminal));
        }

        return choices;
    }

    public static List<ChoiceSetSetting.Choice> GetMinimalDefaultProfileChoices() =>
    [
        new("Default profile for this app", TerminalHostIds.DefaultProfile),
    ];

    public static List<ChoiceSetSetting.Choice> GetDefaultProfileChoices(string terminalApplicationId) =>
        TerminalCatalog.GetDefaultProfileIds(terminalApplicationId)
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

    public static List<ChoiceSetSetting.Choice> GetSettingsChoices() =>
        GetTerminalApplicationChoices();
}
