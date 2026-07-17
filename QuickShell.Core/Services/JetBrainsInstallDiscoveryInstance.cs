namespace QuickShell.Services;

internal sealed class JetBrainsInstallDiscoveryInstance : QuickShell.Abstractions.IInstallDiscovery
{
    public string? TryResolveExecutable(string id)
    {
        if (string.Equals(id, CompanionAppCatalog.PresetRider, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveRider();
        if (string.Equals(id, CompanionAppCatalog.PresetIntelliJIdea, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveIntelliJIdea();
        if (string.Equals(id, CompanionAppCatalog.PresetWebStorm, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveWebStorm();
        if (string.Equals(id, CompanionAppCatalog.PresetPyCharm, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolvePyCharm();
        if (string.Equals(id, CompanionAppCatalog.PresetGoLand, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveGoLand();
        if (string.Equals(id, CompanionAppCatalog.PresetCLion, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveCLion();
        if (string.Equals(id, CompanionAppCatalog.PresetAndroidStudio, StringComparison.OrdinalIgnoreCase)) return JetBrainsInstallDiscovery.TryResolveAndroidStudio();
        return null;
    }

    public string? TryInferPresetFromPath(string? executablePath) => null;
}
