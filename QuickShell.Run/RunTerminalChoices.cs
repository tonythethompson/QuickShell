using QuickShell.Abstractions;
using QuickShell.Services;

namespace QuickShell.Run;

internal static class RunTerminalChoices
{
    public static IReadOnlyList<(string Id, string Label)> GetTerminalApplicationChoices(ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var choices = new List<(string Id, string Label)>
        {
            (TerminalHostIds.LetWindowsChoose, "Let Windows choose"),
            (TerminalHostIds.WindowsTerminal, "Windows Terminal"),
            (TerminalHostIds.WindowsConsoleHost, "Windows Console Host"),
        };

        if (catalog.HasTerminalApplication(TerminalHostIds.IntelligentTerminal))
        {
            choices.Add((TerminalHostIds.IntelligentTerminal, "Intelligent Terminal"));
        }

        return choices;
    }

    public static IReadOnlyList<(string Id, string Label)> GetDefaultProfileChoices(
        ITerminalCatalog catalog,
        string terminalApplicationId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.GetDefaultProfileIds(terminalApplicationId)
            .Select(id => id.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase)
                ? (id, "Default profile for this app")
                : id switch
                {
                    "powershell" => (id, "PowerShell"),
                    "pwsh" => (id, "PowerShell 7"),
                    "cmd" => (id, "Command Prompt"),
                    _ => (id, id),
                })
            .ToList();
    }

    public static IReadOnlyList<(string Id, string Label)> GetLaunchTargetChoices(ITerminalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.GetLaunchTargets(includeDefaultChoice: true)
            .Select(target => (target.Id, target.DisplayName))
            .ToList();
    }
}
