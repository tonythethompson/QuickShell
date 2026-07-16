using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Pages;
using QuickShell.Services;

namespace QuickShell.Commands;

/// <summary>
/// Opens the workspace editor prefilled from a duplicate. The copy is not saved until
/// the user confirms in the form (matches PowerToys Run duplicate behavior).
/// </summary>
internal sealed partial class DuplicateShortcutCommand : ShortcutFormPage
{
    public DuplicateShortcutCommand(TerminalShortcut source, Action onSaved, IQuickShellServices? services = null)
        : base(
            existing: null,
            onSaved,
            createSeed: (services ?? throw new InvalidOperationException("IQuickShellServices is required.")).Shortcuts.BuildDuplicateFrom(source),
            services: services)
    {
        Id = $"com.quickshell.shortcut-form.duplicate.{Guid.NewGuid():N}";
        Name = Strings.Command_Duplicate_Name;
        Icon = new IconInfo(ShortcutGlyphs.Duplicate);
        Title = Strings.DuplicateWorkspace_Title;
    }
}
