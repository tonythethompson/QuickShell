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
    public CreateShortcutCommand(Action onSaved)
        : base(existing: null, onSaved)
    {
        Id = ShortcutCommandIds.CreateShortcut;
    }

    /// <summary>
    /// Create form prefilled from a seed (e.g. discovered git repo). Keeps a unique page id
    /// so multiple seeded create rows can coexist in a list page.
    /// </summary>
    public CreateShortcutCommand(Action onSaved, TerminalShortcut createSeed)
        : base(existing: null, onSaved, createSeed)
    {
    }
}
