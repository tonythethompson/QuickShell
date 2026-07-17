using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class CompanionAppArgumentValidationInstance : ICompanionAppArgumentValidation
{
    public bool ShouldShowArgumentsField(string p, string? ph) => CompanionAppArgumentValidation.ShouldShowArgumentsField(p, ph);
    public string GetArgumentTooltip(string p, string? ph) => CompanionAppArgumentValidation.GetArgumentTooltip(p, ph);
    public string GetArgumentPlaceholder(string p, string? ph) => CompanionAppArgumentValidation.GetArgumentPlaceholder(p, ph);
    public string NormalizeForSave(string p, string? ph, string? a) => CompanionAppArgumentValidation.NormalizeForSave(p, ph, a);
    public string? BuildArgumentWarning(string p, string? ph, string? a, string? d) => CompanionAppArgumentValidation.BuildArgumentWarning(p, ph, a, d);
    public bool TryValidateForSave(string p, string? ph, string? a, out string e) => CompanionAppArgumentValidation.TryValidateForSave(p, ph, a, out e);
}
