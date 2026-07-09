namespace QuickShell.Abstractions.Classification;

internal interface IDevServerDetector
{
    string? TryDetectDevServerUrl(string directory);

    string? TryInferTaskType(string directory);

    string? TryDetectDevLaunchCommand(string directory);

    string FormatPackageScriptCommand(string directory, string scriptName);
}
