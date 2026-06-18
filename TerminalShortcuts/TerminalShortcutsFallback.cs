using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalShortcuts.Pages;

namespace TerminalShortcuts;

internal sealed partial class TerminalShortcutsFallback : FallbackCommandItem
{
    public TerminalShortcutsFallback(TerminalShortcutsPage page)
        : base("com.terminalshortcuts.fallback", "Terminal shortcut")
    {
        Command = page;
    }

    public override void UpdateQuery(string query)
    {
        if (Command is TerminalShortcutsPage page)
        {
            page.UpdateSearchText(string.Empty, query);
        }
    }
}
