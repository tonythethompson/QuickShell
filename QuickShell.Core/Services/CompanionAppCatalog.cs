using System.Text.Json;

namespace QuickShell.Services;

internal static class CompanionAppCatalog
{
    public const string PresetNone = "none";
    public const string PresetCustom = "custom";
    public const string PresetExplorer = "explorer";
    public const string PresetVs2022 = "vs2022";
    public const string PresetVs2026 = "vs2026";
    public const string PresetGitHubDesktop = "github-desktop";
    public const string PresetFork = "fork";
    public const string PresetAzureDataStudio = "azure-data-studio";
    public const string PresetObsidian = "obsidian";
    public const string PresetSublime = "sublime";
    public const string PresetNeovide = "neovide";
    public const string PresetGvim = "gvim";
    public const string PresetRider = "rider";
    public const string PresetIntelliJIdea = "intellij-idea";
    public const string PresetZed = "zed";
    public const string PresetNotepadPlusPlus = "notepad-plus-plus";
    public const string PresetVsCode = "vscode";
    public const string PresetCursor = "cursor";

    private static readonly IReadOnlyList<(string Id, string Title, string DefaultArguments, IReadOnlyList<string> CandidatePaths)> Definitions =
    [
        (PresetExplorer, "Windows Explorer", "{folder}", BuildExplorerCandidates()),
        (PresetVs2022, "Visual Studio 2022", "{solution}", []),
        (PresetVs2026, "Visual Studio 2026", "{solution}", []),
        (PresetGitHubDesktop, "GitHub Desktop", "{folder}", BuildGitHubDesktopCandidates()),
        (PresetFork, "Fork", "{folder}", BuildForkCandidates()),
        (PresetAzureDataStudio, "Azure Data Studio", "{folder}", BuildAzureDataStudioCandidates()),
        (PresetObsidian, "Obsidian", "{folder}", BuildObsidianCandidates()),
        (PresetSublime, "Sublime Text", ".", BuildSublimeCandidates()),
        (PresetNeovide, "Neovide", ".", BuildNeovideCandidates()),
        (PresetGvim, "GVim", ".", BuildGvimCandidates()),
        (PresetRider, "JetBrains Rider", "{folder}", []),
        (PresetIntelliJIdea, "IntelliJ IDEA", "{folder}", []),
        (PresetZed, "Zed", ".", BuildZedCandidates()),
        (PresetNotepadPlusPlus, "Notepad++", string.Empty, BuildNotepadPlusPlusCandidates()),
        (PresetVsCode, "Visual Studio Code", ".", BuildVsCodeCandidates()),
        (PresetCursor, "Cursor", ".", BuildCursorCandidates()),
    ];

    public static bool IsCatalogPreset(string presetId) =>
        !string.Equals(presetId, PresetNone, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(presetId, PresetCustom, StringComparison.OrdinalIgnoreCase);

    public static bool IsPresetInstalled(string presetId)
    {
        if (!IsCatalogPreset(presetId))
        {
            return true;
        }

        return TryResolveExecutable(presetId) is not null;
    }

    /// <summary>
    /// Maps a stored preset to a value the form dropdown can represent when the app is no longer installed.
    /// </summary>
    public static string NormalizePresetForForm(string presetId, string? executablePath)
    {
        if (!IsCatalogPreset(presetId) || IsPresetInstalled(presetId))
        {
            return presetId;
        }

        return string.IsNullOrWhiteSpace(executablePath) ? PresetNone : PresetCustom;
    }

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

        var path = TryResolveExecutablePath(executablePath, out var resolved)
            ? resolved
            : executablePath.Trim();

        var visualStudioPreset = VisualStudioInstallDiscovery.TryInferPresetFromDevenvPath(path);
        if (visualStudioPreset is not null)
        {
            return visualStudioPreset;
        }

        var fileName = Path.GetFileName(path);

        foreach (var (presetId, fileNames) in ExecutableNamePresets)
        {
            if (!fileNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (string.Equals(presetId, PresetExplorer, StringComparison.OrdinalIgnoreCase)
                && !IsWindowsExplorerExecutable(path))
            {
                continue;
            }

            return presetId;
        }

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

        return FindDefinition(preset).Title;
    }

    public static string GetContextMenuIcon(string? executablePath)
    {
        var preset = InferPresetFromPath(executablePath);
        return preset switch
        {
            PresetExplorer => "\uE838",
            PresetVsCode or PresetCursor => "\uE90F",
            PresetVs2022 or PresetVs2026 => "\uEB4D",
            PresetGitHubDesktop or PresetFork => "\uE8C8",
            PresetObsidian or PresetSublime or PresetNotepadPlusPlus => "\uE8A5",
            PresetRider or PresetIntelliJIdea => "\uE90F",
            PresetZed or PresetNeovide or PresetGvim => "\uE90F",
            PresetAzureDataStudio => "\uE943",
            PresetNone or PresetCustom => ShortcutGlyphs.OpenCompanionApp,
            _ => ShortcutGlyphs.OpenCompanionApp,
        };
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

        var definition = FindDefinition(presetId);
        if (definition.Id is null)
        {
            return false;
        }

        executablePath = TryResolveExecutable(presetId);
        arguments = definition.DefaultArguments;
        return executablePath is not null;
    }

    public static string? TryResolveExecutable(string presetId)
    {
        if (string.Equals(presetId, PresetVs2022, StringComparison.OrdinalIgnoreCase))
        {
            return VisualStudioInstallDiscovery.TryResolveDevenv(17, 18);
        }

        if (string.Equals(presetId, PresetVs2026, StringComparison.OrdinalIgnoreCase))
        {
            return VisualStudioInstallDiscovery.TryResolveDevenv(18, 19);
        }

        if (string.Equals(presetId, PresetRider, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolveRider();
        }

        if (string.Equals(presetId, PresetIntelliJIdea, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolveIntelliJIdea();
        }

        var definition = FindDefinition(presetId);
        return definition.Id is null ? null : TryResolveExecutable(definition.CandidatePaths);
    }

    public static string GetDefaultArguments(string presetId)
    {
        var definition = FindDefinition(presetId);
        return definition.Id is null ? string.Empty : definition.DefaultArguments;
    }

    public readonly record struct CompanionAppFormState(
        string Preset,
        string Path,
        string Arguments,
        bool LaunchOnWorkspaceOpen);

    /// <summary>
    /// Re-resolves companion fields when opening the workspace form (handles uninstall / moved installs).
    /// </summary>
    public static CompanionAppFormState ReconcileStoredShortcut(
        bool openOnLaunch,
        string? executablePath,
        string? arguments)
    {
        var path = executablePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
        }

        var state = ReconcileForForm(InferPresetFromPath(path), executablePath, arguments);
        if (string.Equals(state.Preset, PresetNone, StringComparison.OrdinalIgnoreCase))
        {
            return state;
        }

        return state with { LaunchOnWorkspaceOpen = openOnLaunch };
    }

    public static CompanionAppFormState ReconcileForForm(
        string? presetId,
        string? executablePath,
        string? arguments)
    {
        var path = executablePath?.Trim() ?? string.Empty;
        var args = arguments?.Trim() ?? string.Empty;
        var preset = string.IsNullOrWhiteSpace(path)
            ? PresetNone
            : NormalizePresetForForm(presetId ?? InferPresetFromPath(path), path);

        if (string.Equals(preset, PresetNone, StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
        }

        if (IsCatalogPreset(preset) && TryApplyPreset(preset, out var catalogPath, out var catalogArgs))
        {
            return new CompanionAppFormState(preset, catalogPath!, catalogArgs, true);
        }

        if (IsCatalogPreset(preset))
        {
            preset = string.IsNullOrWhiteSpace(path) ? PresetNone : PresetCustom;
            if (string.Equals(preset, PresetNone, StringComparison.OrdinalIgnoreCase))
            {
                return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
            }
        }

        if (string.Equals(preset, PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveExecutablePath(path, out var resolvedPath))
            {
                var resolvedArgs = string.IsNullOrWhiteSpace(args)
                    ? GetDefaultArguments(InferPresetFromPath(resolvedPath))
                    : args;
                return new CompanionAppFormState(PresetCustom, resolvedPath, resolvedArgs, true);
            }

            return new CompanionAppFormState(PresetCustom, path, args, false);
        }

        return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
    }

    public static CompanionAppFormState CreateStateFromPreset(string presetId)
    {
        if (string.Equals(presetId, PresetNone, StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
        }

        if (string.Equals(presetId, PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            return new CompanionAppFormState(PresetCustom, string.Empty, string.Empty, false);
        }

        if (TryApplyPreset(presetId, out var path, out var args))
        {
            return new CompanionAppFormState(presetId, path!, args, true);
        }

        return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
    }

    public static CompanionAppFormState ReconcileForSave(
        string? presetId,
        string? executablePath,
        string? arguments,
        bool openOnLaunch)
    {
        var state = ReconcileForForm(presetId, executablePath, arguments);
        if (string.Equals(state.Preset, PresetNone, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(state.Path))
        {
            return new CompanionAppFormState(PresetNone, string.Empty, string.Empty, false);
        }

        return state with { LaunchOnWorkspaceOpen = openOnLaunch };
    }

    public static bool ShouldShowExecutablePath(string preset, string? path) =>
        string.Equals(preset, PresetCustom, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(path);

    public static bool ShouldShowPathWarning(string preset, string? path) =>
        string.Equals(preset, PresetCustom, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(path)
        && !TryResolveExecutablePath(path, out _);

    public static string BuildPathWarning(string preset, string? path) =>
        ShouldShowPathWarning(preset, path)
            ? "Executable not found. Choose another app or set App preset to None."
            : string.Empty;

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

    private static (string Id, string Title, string DefaultArguments, IReadOnlyList<string> CandidatePaths) FindDefinition(string presetId) =>
        Definitions.FirstOrDefault(item =>
            string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));

    private static readonly (string PresetId, string[] FileNames)[] ExecutableNamePresets =
    [
        (PresetExplorer, ["explorer.exe"]),
        (PresetGitHubDesktop, ["GitHubDesktop.exe"]),
        (PresetFork, ["Fork.exe"]),
        (PresetAzureDataStudio, ["azuredatastudio.exe"]),
        (PresetObsidian, ["Obsidian.exe"]),
        (PresetSublime, ["sublime_text.exe", "subl.exe"]),
        (PresetNeovide, ["neovide.exe"]),
        (PresetGvim, ["gvim.exe"]),
        (PresetRider, ["rider64.exe"]),
        (PresetIntelliJIdea, ["idea64.exe"]),
        (PresetZed, ["zed.exe", "Zed.exe"]),
        (PresetNotepadPlusPlus, ["notepad++.exe"]),
        (PresetVsCode, ["Code.exe"]),
        (PresetCursor, ["Cursor.exe"]),
    ];

    private static bool IsWindowsExplorerExecutable(string path)
    {
        if (TryResolveExecutablePath(path, out var resolved))
        {
            path = resolved;
        }

        try
        {
            var windowsDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return directory is not null
                && string.Equals(directory, windowsDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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

    private static IReadOnlyList<string> BuildExplorerCandidates() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
    ];

    private static IReadOnlyList<string> BuildGitHubDesktopCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "GitHubDesktop", "GitHubDesktop.exe"),
            Path.Combine(localAppData, "GitHub Desktop", "GitHubDesktop.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildForkCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return
        [
            Path.Combine(localAppData, "Fork", "Fork.exe"),
            Path.Combine(programFiles, "Fork", "Fork.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildAzureDataStudioCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return
        [
            Path.Combine(localAppData, "Programs", "Azure Data Studio", "azuredatastudio.exe"),
            Path.Combine(localAppData, "Programs", "Azure Data Studio", "bin", "azuredatastudio.exe"),
            Path.Combine(programFiles, "Azure Data Studio", "bin", "azuredatastudio.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildObsidianCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return
        [
            Path.Combine(localAppData, "Obsidian", "Obsidian.exe"),
            Path.Combine(programFiles, "Obsidian", "Obsidian.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildSublimeCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return
        [
            Path.Combine(programFiles, "Sublime Text", "sublime_text.exe"),
            Path.Combine(programFilesX86, "Sublime Text", "sublime_text.exe"),
            Path.Combine(programFiles, "Sublime Text 3", "sublime_text.exe"),
            "subl.exe",
            "sublime_text.exe",
        ];
    }

    private static IReadOnlyList<string> BuildNeovideCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "Programs", "neovide", "neovide.exe"),
            Path.Combine(localAppData, "Programs", "Neovide", "neovide.exe"),
            "neovide.exe",
        ];
    }

    private static IReadOnlyList<string> BuildGvimCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return
        [
            "gvim.exe",
            Path.Combine(programFiles, "Vim", "vim91", "gvim.exe"),
            Path.Combine(programFiles, "Vim", "vim92", "gvim.exe"),
            Path.Combine(programFiles, "Vim", "vim90", "gvim.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildZedCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "Programs", "Zed", "zed.exe"),
            Path.Combine(localAppData, "Programs", "Zed", "Zed.exe"),
            Path.Combine(localAppData, "Programs", "zed", "zed.exe"),
            "zed.exe",
        ];
    }

    private static IReadOnlyList<string> BuildNotepadPlusPlusCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return
        [
            Path.Combine(programFiles, "Notepad++", "notepad++.exe"),
            Path.Combine(programFilesX86, "Notepad++", "notepad++.exe"),
        ];
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
