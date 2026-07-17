using QuickShell.Models;

namespace QuickShell.Abstractions;

internal interface ICompanionAppNormalization
{
    const int MaxCompanionCount = 5;
    void EnsureCompanionsFromLegacy(TerminalShortcut shortcut);
    void MirrorLegacyFieldsFromPrimary(TerminalShortcut shortcut);
    void NormalizeCompanions(TerminalShortcut shortcut);
    void ApplyPrimaryFromScalars(TerminalShortcut shortcut, bool openOnLaunch, string? path, string? arguments, IReadOnlyList<CompanionAppEntry>? preserveAdditionalFrom = null);
    IReadOnlyList<CompanionAppEntry> GetConfigured(TerminalShortcut shortcut);
    IReadOnlyList<CompanionAppEntry> GetOpenOnLaunch(TerminalShortcut shortcut);
    CompanionAppEntry? GetPrimary(TerminalShortcut shortcut);
    bool TryValidateCompanions(TerminalShortcut shortcut, out string error);
    CompanionAppEntry CloneEntry(CompanionAppEntry entry);
}
