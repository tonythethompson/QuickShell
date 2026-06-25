using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Pages;

namespace QuickShell;

internal sealed partial class QuickShellFallback : FallbackCommandItem
{
    public QuickShellFallback(QuickShellFallbackPage page)
        : base("com.quickshell.fallback", "Saved shortcut")
    {
        Command = page;
    }

    public override void UpdateQuery(string query)
    {
        if (Command is QuickShellFallbackPage page)
        {
            page.UpdateSearchText(string.Empty, query);
        }
    }
}
