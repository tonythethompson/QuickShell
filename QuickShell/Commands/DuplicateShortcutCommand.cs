using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

/// <summary>
/// Opens the workspace editor prefilled from a duplicate. The copy is not saved until
/// the user confirms in the form (matches PowerToys Run duplicate behavior).
/// </summary>
internal sealed partial class DuplicateShortcutCommand : ShortcutFormPage
{
    public DuplicateShortcutCommand(string sourceName, Action onSaved)
        : base(existing: null, onSaved, createSeed: QuickShellRuntimeServices.Shortcuts.BuildDuplicate(sourceName))
    {
        Id = $"com.quickshell.shortcut-form.duplicate.{Guid.NewGuid():N}";
        Name = "Duplicate";
        Icon = new IconInfo(ShortcutGlyphs.Duplicate);
        Title = "Duplicate workspace";
    }
}
