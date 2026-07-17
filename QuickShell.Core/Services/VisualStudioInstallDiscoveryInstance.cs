using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class VisualStudioInstallDiscoveryInstance : IInstallDiscovery
{
    public string? TryResolveExecutable(string id)
    {
        if (string.Equals(id, ICompanionAppCatalog.PresetVs2022, StringComparison.OrdinalIgnoreCase)) return VisualStudioInstallDiscovery.TryResolveDevenv(17, 18);
        if (string.Equals(id, ICompanionAppCatalog.PresetVs2026, StringComparison.OrdinalIgnoreCase)) return VisualStudioInstallDiscovery.TryResolveDevenv(18, 19);
        return null;
    }

    public string? TryInferPresetFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Replace('/', '\\');
        if (normalized.Contains(@"\2022\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Visual Studio 2022", StringComparison.OrdinalIgnoreCase))
        {
            return ICompanionAppCatalog.PresetVs2022;
        }

        if (normalized.Contains(@"\2026\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Visual Studio 2026", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\18.", StringComparison.OrdinalIgnoreCase))
        {
            return ICompanionAppCatalog.PresetVs2026;
        }

        return null;
    }
}
