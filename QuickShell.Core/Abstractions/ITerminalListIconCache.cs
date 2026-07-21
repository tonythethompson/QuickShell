using QuickShell.Models;

namespace QuickShell.Abstractions;

internal interface ITerminalListIconCache
{
    string? TryResolveUpgradedListIcon(TerminalShortcut shortcut);

    string PrepareForList(string icon);

    void PrewarmProfiles();
}
