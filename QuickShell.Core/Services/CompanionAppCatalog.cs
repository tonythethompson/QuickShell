using System.Text.Json;

namespace QuickShell.Services;

internal static class CompanionAppCatalog
{
    public const string PresetNone = "none";
    public const string PresetCustom = "custom";
    public const string PresetVsCode = "vscode";
    public const string PresetCursor = "cursor";
    public const string PresetNotepad = "notepad";

    private static readonly IReadOnlyList<(string Id, string Title, string DefaultArguments, IReadOnlyList<string> CandidatePaths)> Definitions =
    [
        (PresetVsCode, "Visual Studio Code", ".", BuildVsCodeCandidates()),
        (PresetCursor, "Cursor", ".", BuildCursorCandidates()),
        (PresetNotepad, "Notepad", string.Empty, ["notepad.exe"]),
    ];

    public static string BuildFormChoicesJson()
    {
        var choices = new List<object>
        {
            new { title = "None", value = PresetNone },
        };

        foreach (var definition in Definitions)
        {
            choices.Add(new { title = definition.Title, value = definition.Id });
        }

        choices.Add(new { title = "Custom…", value = PresetCustom });

        return JsonSerializer.Serialize(choices);
    }

    public static string InferPresetFromPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return PresetNone;
        }

        var fileName = Path.GetFileName(executablePath).Trim();

        foreach (var definition in Definitions)
        {
            foreach (var candidate in definition.CandidatePaths)
            {
                if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return definition.Id;
                }
            }
        }

        return PresetCustom;
    }

    public static string GetDisplayName(string? executablePath)
    {
        var preset = InferPresetFromPath(executablePath);
        if (preset is PresetNone or PresetCustom)
        {
            return string.IsNullOrWhiteSpace(executablePath)
                ? "Companion app"
                : Path.GetFileNameWithoutExtension(executablePath);
        }

        return Definitions.First(definition => definition.Id == preset).Title;
    }

    public static string GetContextMenuIcon(string? executablePath)
    {
        _ = InferPresetFromPath(executablePath);
        return ShortcutGlyphs.OpenCompanionApp;
    }

    public static bool TryApplyPreset(string presetId, out string? executablePath, out string arguments)
    {
        executablePath = null;
        arguments = string.Empty;

        if (string.Equals(presetId, PresetNone, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(presetId, PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
        if (definition.Id is null)
        {
            return false;
        }

        executablePath = TryResolveExecutable(definition.CandidatePaths);
        arguments = definition.DefaultArguments;
        return executablePath is not null;
    }

    public static string? TryResolveExecutable(string presetId)
    {
        var definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
        return definition.Id is null ? null : TryResolveExecutable(definition.CandidatePaths);
    }

    public static string GetDefaultArguments(string presetId)
    {
        var definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
        return definition.Id is null ? string.Empty : definition.DefaultArguments;
    }

    public static bool TryResolveExecutablePath(string? executablePath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
        if (File.Exists(expanded))
        {
            resolvedPath = Path.GetFullPath(expanded);
            return true;
        }

        var fileName = Path.GetFileName(expanded);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (TryFindOnPath(fileName, out var onPath))
        {
            resolvedPath = onPath;
            return true;
        }

        return false;
    }

    private static string? TryResolveExecutable(IReadOnlyList<string> candidatePaths)
    {
        foreach (var candidate in candidatePaths)
        {
            if (TryResolveExecutablePath(candidate, out var resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool TryFindOnPath(string fileName, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        foreach (var segment in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, fileName);
                if (File.Exists(candidate))
                {
                    resolvedPath = Path.GetFullPath(candidate);
                    return true;
                }
            }
            catch
            {
                // Skip invalid PATH segments.
            }
        }

        return false;
    }

    private static IReadOnlyList<string> BuildVsCodeCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return
        [
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildCursorCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe"),
            Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe"),
        ];
    }
}
