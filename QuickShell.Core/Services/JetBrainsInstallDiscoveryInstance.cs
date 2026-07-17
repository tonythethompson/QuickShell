using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class JetBrainsInstallDiscoveryInstance : IInstallDiscovery
{
    public string? TryResolveExecutable(string id)
    {
        if (string.Equals(id, ICompanionAppCatalog.PresetRider, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveRider();
        if (string.Equals(id, ICompanionAppCatalog.PresetIntelliJIdea, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveIntelliJIdea();
        if (string.Equals(id, ICompanionAppCatalog.PresetWebStorm, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveWebStorm();
        if (string.Equals(id, ICompanionAppCatalog.PresetPyCharm, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolvePyCharm();
        if (string.Equals(id, ICompanionAppCatalog.PresetGoLand, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveGoLand();
        if (string.Equals(id, ICompanionAppCatalog.PresetCLion, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveCLion();
        if (string.Equals(id, ICompanionAppCatalog.PresetAndroidStudio, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveAndroidStudio();
        return null;
    }

    public string? TryInferPresetFromPath(string? executablePath) => null;
}
