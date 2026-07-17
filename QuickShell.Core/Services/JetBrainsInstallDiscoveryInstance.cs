using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class JetBrainsInstallDiscoveryInstance : IInstallDiscovery
{
    public string? TryResolveExecutable(string id)
    {
        if (string.Equals(id, ICompanionAppCatalog.PresetRider, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveRider();
        if (string.Equals(id, ICompanionAppCatalog.PresetIntelliJIdea, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveIntelliJIdea();
        if (string.Equals(id, ICompanionAppCatalog.PresetWebStorm, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveWebStorm();
        if (string.Equals(id, ICompanionAppCatalog.PresetPyCharm, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolvePyCharm();
        if (string.Equals(id, ICompanionAppCatalog.PresetGoLand, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveGoLand();
        if (string.Equals(id, ICompanionAppCatalog.PresetCLion, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveCLion();
        if (string.Equals(id, ICompanionAppCatalog.PresetAndroidStudio, System.StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveAndroidStudio();
        return null;
    }
    public string? TryInferPresetFromPath(string? executablePath) => null;
}
