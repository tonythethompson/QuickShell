namespace QuickShell.Abstractions;

internal interface ICompanionAppArgumentValidation
{
    const string FieldLabel = "Arguments (optional)";
    const string CustomArgumentHelp = "Launch arguments. Use . or {folder} for the workspace path, {solution} for a .sln file.";
    bool ShouldShowArgumentsField(string preset, string? path);
    string GetArgumentTooltip(string preset, string? path);
    string GetArgumentPlaceholder(string preset, string? path);
    string NormalizeForSave(string preset, string? path, string? arguments);
    string? BuildArgumentWarning(string preset, string? path, string? arguments, string? workspaceDirectory);
    bool TryValidateForSave(string preset, string? path, string? arguments, out string error);
}
