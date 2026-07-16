using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

/// <summary>
/// Opens a fresh create-shortcut form. Separate type so Command Palette does not reuse
/// the same navigation slot as edit forms.
/// </summary>
internal sealed partial class CreateShortcutCommand : ShortcutFormPage
{
    public CreateShortcutCommand(Action onSaved, IQuickShellServices? services = null)
        : base(existing: null, onSaved, services: services)
    {
        Id = ShortcutCommandIds.CreateShortcut;
    }

    /// <summary>
    /// Create form prefilled from a seed (e.g. discovered git repo). Uses a stable command id
    /// derived from the repo directory so CmdPal Pin to home can resolve the command later.
    /// </summary>
    public CreateShortcutCommand(Action onSaved, TerminalShortcut createSeed, IQuickShellServices? services = null)
        : base(existing: null, onSaved, createSeed, services)
    {
        if (!string.IsNullOrWhiteSpace(createSeed.Directory))
        {
            Id = ShortcutCommandIds.DiscoverCreate(createSeed.Directory);
        }
    }
}
