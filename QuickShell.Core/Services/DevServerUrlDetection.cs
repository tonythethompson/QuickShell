using QuickShell.Abstractions.Classification;
using QuickShell.Classification.Detectors;

namespace QuickShell.Services;

internal static class DevServerUrlDetection
{
    private static readonly DevServerDetector Default = new();

    public static string? TryDetectDevServerUrl(string directory) =>
        Default.TryDetectDevServerUrl(directory);

    public static string? TryInferTaskType(string directory) =>
        Default.TryInferTaskType(directory);

    public static string? TryDetectDevLaunchCommand(string directory) =>
        Default.TryDetectDevLaunchCommand(directory);

    internal static string FormatPackageScriptCommand(string directory, string scriptName) =>
        Default.FormatPackageScriptCommand(directory, scriptName);
}
