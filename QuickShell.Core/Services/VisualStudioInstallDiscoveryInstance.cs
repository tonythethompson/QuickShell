using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class VisualStudioInstallDiscoveryInstance : IInstallDiscovery
{
    public string? TryResolveExecutable(string id)
    {
        if (string.Equals(id, ICompanionAppCatalog.PresetVs2022, System.StringComparison.OrdinalIgnoreCase)) return VisualStudioInstallDiscovery.TryResolveDevenv(17, 18);
        if (string.Equals(id, ICompanionAppCatalog.PresetVs2026, System.StringComparison.OrdinalIgnoreCase)) return VisualStudioInstallDiscovery.TryResolveDevenv(18, 19);
        return null;
    }
    public string? TryInferPresetFromPath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        var n = p.Replace('/', '\\');
        if (n.Contains(@"\2022\", System.StringComparison.OrdinalIgnoreCase) || n.Contains("Visual Studio 2022", System.StringComparison.OrdinalIgnoreCase)) return ICompanionAppCatalog.PresetVs2022;
        if (n.Contains(@"\2026\", System.StringComparison.OrdinalIgnoreCase) || n.Contains("Visual Studio 2026", System.StringComparison.OrdinalIgnoreCase) || n.Contains(@"\18.", System.StringComparison.OrdinalIgnoreCase)) return ICompanionAppCatalog.PresetVs2026;
        return null;
    }
}
