namespace QuickShell.Abstractions;

internal interface IInstallDiscovery
{
    string? TryResolveExecutable(string presetId);
    string? TryInferPresetFromPath(string? executablePath);
}
