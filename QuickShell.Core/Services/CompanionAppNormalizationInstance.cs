using QuickShell.Abstractions;
using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class CompanionAppNormalizationInstance : ICompanionAppNormalization
{
    public void EnsureCompanionsFromLegacy(TerminalShortcut s) => CompanionAppNormalization.EnsureCompanionsFromLegacy(s);
    public void MirrorLegacyFieldsFromPrimary(TerminalShortcut s) => CompanionAppNormalization.MirrorLegacyFieldsFromPrimary(s);
    public void NormalizeCompanions(TerminalShortcut s) => CompanionAppNormalization.NormalizeCompanions(s);
    public void ApplyPrimaryFromScalars(TerminalShortcut s, bool o, string? p, string? a, IReadOnlyList<CompanionAppEntry>? pr = null) => CompanionAppNormalization.ApplyPrimaryFromScalars(s, o, p, a, pr);
    public IReadOnlyList<CompanionAppEntry> GetConfigured(TerminalShortcut s) => CompanionAppNormalization.GetConfigured(s);
    public IReadOnlyList<CompanionAppEntry> GetOpenOnLaunch(TerminalShortcut s) => CompanionAppNormalization.GetOpenOnLaunch(s);
    public CompanionAppEntry? GetPrimary(TerminalShortcut s) => CompanionAppNormalization.GetPrimary(s);
    public bool TryValidateCompanions(TerminalShortcut s, out string e) => CompanionAppNormalization.TryValidateCompanions(s, out e);
    public CompanionAppEntry CloneEntry(CompanionAppEntry e) => CompanionAppNormalization.CloneEntry(e);
}
