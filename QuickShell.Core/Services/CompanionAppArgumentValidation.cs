using System.Text.RegularExpressions;

namespace QuickShell.Services;

internal static partial class CompanionAppArgumentValidation
{
    public const string FieldLabel = "Arguments (optional)";
    public const string CustomArgumentHelp =
        "Launch arguments. Use . or {folder} for the workspace path, {solution} for a .sln file.";

    public static bool ShouldShowArgumentsField(string preset, string? path)
    {
        if (string.Equals(preset, CompanionAppCatalog.PresetNone, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(preset, CompanionAppCatalog.PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(path);
        }

        if (CompanionAppCatalog.IsCatalogPreset(preset))
        {
            return !string.IsNullOrWhiteSpace(path)
                || CompanionAppCatalog.TryApplyPreset(preset, out _, out _);
        }

        return false;
    }

    public static string GetArgumentTooltip(string preset, string? path) =>
        ResolveRuleSet(preset, path).Tooltip;

    public static string GetArgumentPlaceholder(string preset, string? path) =>
        ResolveRuleSet(preset, path).Placeholder;

    public static string NormalizeForSave(string preset, string? path, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return ResolveRuleSet(preset, path).DefaultArguments;
        }

        return arguments.Trim();
    }

    public static string? BuildArgumentWarning(
        string preset,
        string? path,
        string? arguments,
        string? workspaceDirectory)
    {
        if (!ShouldShowArgumentsField(preset, path))
        {
            return null;
        }

        var ruleSet = ResolveRuleSet(preset, path);
        var normalized = string.IsNullOrWhiteSpace(arguments) ? ruleSet.DefaultArguments : arguments.Trim();
        return TryGetPresetMismatchWarning(ruleSet, normalized, workspaceDirectory);
    }

    public static bool TryValidateForSave(string preset, string? path, string? arguments, out string error)
    {
        error = string.Empty;
        if (!ShouldShowArgumentsField(preset, path))
        {
            return true;
        }

        var normalized = NormalizeForSave(preset, path, arguments);
        if (normalized.Length > ShortcutValidation.MaxCompanionAppArgumentsLength)
        {
            error = $"Companion app arguments must be {ShortcutValidation.MaxCompanionAppArgumentsLength} characters or fewer.";
            return false;
        }

        if (normalized.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            error = "Companion app arguments cannot contain line breaks.";
            return false;
        }

        if (ContainsUnknownPlaceholder(normalized))
        {
            error = "Companion app arguments support only {folder} and {solution} placeholders.";
            return false;
        }

        return true;
    }

    private static string? TryGetPresetMismatchWarning(
        ArgumentRuleSet ruleSet,
        string arguments,
        string? workspaceDirectory)
    {
        if (ruleSet.ExpectSolutionToken
            && !arguments.Contains("{solution}", StringComparison.OrdinalIgnoreCase)
            && WorkspaceCompanionSignals.TryFindSolutionFile(workspaceDirectory ?? string.Empty) is not null)
        {
            return "This app usually opens with {solution} when a solution file is present.";
        }

        if (ruleSet.ExpectSolutionToken
            && arguments.Contains("{solution}", StringComparison.OrdinalIgnoreCase)
            && WorkspaceCompanionSignals.TryFindSolutionFile(workspaceDirectory ?? string.Empty) is null
            && !string.IsNullOrWhiteSpace(workspaceDirectory))
        {
            return "No .sln file found in the workspace folder; {solution} will use the folder path instead.";
        }

        if (ruleSet.ExpectFolderToken
            && !arguments.Contains("{folder}", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(arguments, ".", StringComparison.Ordinal))
        {
            return "This app usually opens with {folder}.";
        }

        if (ruleSet.ExpectDotShorthand
            && !string.Equals(arguments, ".", StringComparison.Ordinal)
            && !arguments.Contains("{folder}", StringComparison.OrdinalIgnoreCase))
        {
            return "This app usually opens with . (workspace folder).";
        }

        return null;
    }

    private static bool ContainsUnknownPlaceholder(string arguments)
    {
        foreach (Match match in PlaceholderRegex().Matches(arguments))
        {
            var token = match.Groups[1].Value;
            if (!token.Equals("folder", StringComparison.OrdinalIgnoreCase)
                && !token.Equals("solution", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ArgumentRuleSet ResolveRuleSet(string preset, string? path)
    {
        if (string.Equals(preset, CompanionAppCatalog.PresetCustom, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(path))
        {
            preset = CompanionAppCatalog.InferPresetFromPath(path);
        }

        if (!CompanionAppCatalog.IsCatalogPreset(preset))
        {
            return new ArgumentRuleSet(
                string.Empty,
                ".",
                CustomArgumentHelp,
                ExpectFolderToken: false,
                ExpectDotShorthand: false,
                ExpectSolutionToken: false);
        }

        return preset switch
        {
            CompanionAppCatalog.PresetVs2022 or CompanionAppCatalog.PresetVs2026 => new ArgumentRuleSet(
                "{solution}",
                "{solution}",
                "Use {solution} for the .sln file, or the folder if none exists.",
                ExpectFolderToken: false,
                ExpectDotShorthand: false,
                ExpectSolutionToken: true),
            CompanionAppCatalog.PresetExplorer
                or CompanionAppCatalog.PresetGitHubDesktop
                or CompanionAppCatalog.PresetFork
                or CompanionAppCatalog.PresetGitKraken
                or CompanionAppCatalog.PresetSourcetree
                or CompanionAppCatalog.PresetAzureDataStudio
                or CompanionAppCatalog.PresetObsidian
                or CompanionAppCatalog.PresetRider
                or CompanionAppCatalog.PresetIntelliJIdea
                or CompanionAppCatalog.PresetWebStorm
                or CompanionAppCatalog.PresetPyCharm
                or CompanionAppCatalog.PresetGoLand
                or CompanionAppCatalog.PresetCLion
                or CompanionAppCatalog.PresetAndroidStudio
                or CompanionAppCatalog.PresetNotepadPlusPlus => new ArgumentRuleSet(
                "{folder}",
                "{folder}",
                "Use {folder} for the workspace path.",
                ExpectFolderToken: true,
                ExpectDotShorthand: false,
                ExpectSolutionToken: false),
            CompanionAppCatalog.PresetVsCode
                or CompanionAppCatalog.PresetVsCodeInsiders
                or CompanionAppCatalog.PresetCursor
                or CompanionAppCatalog.PresetAntigravity
                or CompanionAppCatalog.PresetDevin
                or CompanionAppCatalog.PresetKiro
                or CompanionAppCatalog.PresetSublime
                or CompanionAppCatalog.PresetNeovide
                or CompanionAppCatalog.PresetGvim
                or CompanionAppCatalog.PresetZed => new ArgumentRuleSet(
                ".",
                ".",
                "Use . to open the workspace folder.",
                ExpectFolderToken: false,
                ExpectDotShorthand: true,
                ExpectSolutionToken: false),
            _ => new ArgumentRuleSet(
                CompanionAppCatalog.GetDefaultArguments(preset),
                CompanionAppCatalog.GetDefaultArguments(preset),
                CustomArgumentHelp,
                ExpectFolderToken: false,
                ExpectDotShorthand: false,
                ExpectSolutionToken: false),
        };
    }

    private readonly record struct ArgumentRuleSet(
        string DefaultArguments,
        string Placeholder,
        string Tooltip,
        bool ExpectFolderToken,
        bool ExpectDotShorthand,
        bool ExpectSolutionToken);

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
