using System.Text.Json;
using QuickShell.Models;

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
    public const string PresetWebStorm = "webstorm";
    public const string PresetPyCharm = "pycharm";
    public const string PresetGoLand = "goland";
    public const string PresetCLion = "clion";
    public const string PresetAndroidStudio = "android-studio";
    public const string PresetZed = "zed";
    public const string PresetNotepadPlusPlus = "notepad-plus-plus";
    public const string PresetVsCode = "vscode";
    public const string PresetVsCodeInsiders = "vscode-insiders";
    public const string PresetCursor = "cursor";
    public const string PresetTrae = "trae";
    public const string PresetGitKraken = "gitkraken";
    public const string PresetSourcetree = "sourcetree";
    public const string PresetAntigravity = "antigravity";
    public const string PresetDevin = "devin";
    public const string PresetKiro = "kiro";

    private static readonly IReadOnlyList<(string Id, string Title, string DefaultArguments, IReadOnlyList<string> CandidatePaths)> Definitions =
    [
        (PresetExplorer, "Windows Explorer", "{folder}", BuildExplorerCandidates()),
        (PresetVs2022, "Visual Studio 2022", "{solution}", []),
        (PresetVs2026, "Visual Studio 2026", "{solution}", []),
        (PresetGitHubDesktop, "GitHub Desktop", "{folder}", BuildGitHubDesktopCandidates()),
        (PresetFork, "Fork", "{folder}", BuildForkCandidates()),
        (PresetGitKraken, "GitKraken", "{folder}", BuildGitKrakenCandidates()),
        (PresetSourcetree, "Sourcetree", "{folder}", BuildSourcetreeCandidates()),
        (PresetAzureDataStudio, "Azure Data Studio", "{folder}", BuildAzureDataStudioCandidates()),
        (PresetObsidian, "Obsidian", "{folder}", BuildObsidianCandidates()),
        (PresetSublime, "Sublime Text", ".", BuildSublimeCandidates()),
        (PresetNeovide, "Neovide", ".", BuildNeovideCandidates()),
        (PresetGvim, "GVim", ".", BuildGvimCandidates()),
        (PresetRider, "JetBrains Rider", "{folder}", []),
        (PresetIntelliJIdea, "IntelliJ IDEA", "{folder}", []),
        (PresetWebStorm, "WebStorm", "{folder}", []),
        (PresetPyCharm, "PyCharm", "{folder}", []),
        (PresetGoLand, "GoLand", "{folder}", []),
        (PresetCLion, "CLion", "{folder}", []),
        (PresetAndroidStudio, "Android Studio", "{folder}", []),
        (PresetZed, "Zed", ".", BuildZedCandidates()),
        (PresetNotepadPlusPlus, "Notepad++", "{folder}", BuildNotepadPlusPlusCandidates()),
        (PresetVsCode, "Visual Studio Code", ".", BuildVsCodeCandidates()),
        (PresetVsCodeInsiders, "VS Code Insiders", ".", BuildVsCodeInsidersCandidates()),
        (PresetCursor, "Cursor", ".", BuildCursorCandidates()),
        (PresetTrae, "TRAE", ".", BuildTraeCandidates()),
        (PresetAntigravity, "Antigravity", ".", BuildAntigravityCandidates()),
        (PresetDevin, "Devin", ".", BuildDevinCandidates()),
        (PresetKiro, "Kiro", ".", BuildKiroCandidates()),
    ];

    public static bool IsCatalogPreset(string presetId) =>
        !string.IsNullOrWhiteSpace(presetId) && FindDefinition(presetId).Id is not null;

    private static Func<string, string?>? _tryResolveExecutableOverride;

    /// <summary>Test hook replacing disk/install resolution for a preset id.</summary>
    internal static Func<string, string?>? TryResolveExecutableOverride
    {
        get => _tryResolveExecutableOverride;
        set
        {
            _tryResolveExecutableOverride = value;
            // Drop process-wide caches so tests with overrides do not see production probes.
            InvalidateInstallCaches();
        }
    }

    private static readonly object FormChoicesLock = new();
    private static string? _cachedFormChoicesJson;
    private static IReadOnlyList<(string Id, string Title)>? _cachedFormChoices;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> PresetExecutableCache =
        new(StringComparer.OrdinalIgnoreCase);

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

    public const string FormChoiceTitleNone = "No companion app";
    public const string FormChoiceTitleCustom = "Custom app";
    public const string BrowseActionTitle = "Choose custom app…";
    public const string BrowseRequiredMessage = "Choose custom app… to pick an executable.";

    /// <summary>
    /// Dropdown choices for the workspace form. Cached — probing every catalog app
    /// (vswhere, JetBrains Toolbox, PATH walks) is too expensive to repeat per form open.
    /// </summary>
    public static string BuildFormChoicesJson()
    {
        if (_tryResolveExecutableOverride is not null)
        {
            return SerializeFormChoices(BuildInstalledFormChoicesUncached());
        }

        EnsureFormChoicesCached();
        return _cachedFormChoicesJson!;
    }

    public static IReadOnlyList<(string Id, string Title)> GetInstalledFormChoices()
    {
        if (_tryResolveExecutableOverride is not null)
        {
            return BuildInstalledFormChoicesUncached();
        }

        EnsureFormChoicesCached();
        return _cachedFormChoices!;
    }

    private static void EnsureFormChoicesCached()
    {
        lock (FormChoicesLock)
        {
            if (_cachedFormChoicesJson is not null && _cachedFormChoices is not null)
            {
                return;
            }
        }

        var choices = BuildInstalledFormChoicesUncached();
        var json = SerializeFormChoices(choices);

        lock (FormChoicesLock)
        {
            _cachedFormChoices = choices;
            _cachedFormChoicesJson = json;
        }
    }

    /// <summary>
    /// Serializes form choices into the JSON representation used by the companion app selector.
    /// </summary>
    /// <param name="choices">The form choice identifiers and display titles.</param>
    /// <returns>The serialized form choices.</returns>
    private static string SerializeFormChoices(IReadOnlyList<(string Id, string Title)> choices) =>
JsonSerializer.Serialize(
    choices.Select(choice => new FormChoiceJson(choice.Title, choice.Id)).ToList(),
    QuickShellJsonContext.Default.ListFormChoiceJson);

    /// <summary>
    /// Builds the form choices for installed companion apps, including the options for no companion app and a custom app.
    /// </summary>
    /// <returns>The available companion-app choices.</returns>
    private static List<(string Id, string Title)> BuildInstalledFormChoicesUncached()
    {
        var choices = new List<(string Id, string Title)>
        {
            (PresetNone, FormChoiceTitleNone),
        };

        foreach (var definition in Definitions)
        {
            if (IsPresetInstalled(definition.Id))
            {
                choices.Add((definition.Id, definition.Title));
            }
        }

        choices.Add((PresetCustom, FormChoiceTitleCustom));
        return choices;
    }

    /// <summary>Drops form-choice and per-preset path caches (tests / after install changes).</summary>
    public static void InvalidateInstallCaches()
    {
        lock (FormChoicesLock)
        {
            _cachedFormChoicesJson = null;
            _cachedFormChoices = null;
        }

        PresetExecutableCache.Clear();
        VisualStudioInstallDiscovery.InvalidateCache();
        JetBrainsInstallDiscovery.InvalidateCache();
    }

    /// <summary>
    /// Maps stored companion preset to the dropdown value shown in the workspace form.
    /// Custom app stays selected for browsed paths even when the filename matches a catalog app.
    /// </summary>
    public static string ToFormPresetValue(string presetId, string? executablePath)
    {
        if (string.Equals(presetId, PresetCustom, StringComparison.OrdinalIgnoreCase))
        {
            return PresetCustom;
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var inferred = InferPresetFromPath(executablePath);
            if (IsCatalogPreset(inferred))
            {
                return inferred;
            }
        }

        return presetId;
    }

    /// <summary>
    /// After browse, use a matching catalog preset or fall back to Custom app.
    /// </summary>
    public static string ResolvePresetAfterBrowse(string selectedPath)
    {
        var inferred = InferPresetFromPath(selectedPath);
        return IsCatalogPreset(inferred) ? inferred : PresetCustom;
    }

    public static bool ShouldShowExecutablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path);

    public static bool ShouldShowPathWarning(string preset, string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !TryResolveExecutablePath(path, out _);

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

        var byName = InferPresetFromFileName(path);
        if (string.Equals(byName, PresetExplorer, StringComparison.OrdinalIgnoreCase)
            && !IsWindowsExplorerExecutable(path))
        {
            return PresetCustom;
        }

        return byName;
    }

    /// <summary>
    /// Filename-only preset guess for form open (no PATH/disk resolution).
    /// </summary>
    public static string InferPresetFromFileName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return PresetNone;
        }

        var path = executablePath.Trim();
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return PresetCustom;
        }

        if (string.Equals(fileName, "devenv.exe", StringComparison.OrdinalIgnoreCase))
        {
            var vs = VisualStudioInstallDiscovery.TryInferPresetFromDevenvPath(path);
            if (vs is not null)
            {
                return vs;
            }
        }

        foreach (var (presetId, fileNames) in ExecutableNamePresets)
        {
            if (fileNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                return presetId;
            }
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

    public static string GetContextMenuIcon(string? executablePath) =>
        ShortcutGlyphs.OpenCompanionApp;

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
        if (TryResolveExecutableOverride is { } resolveOverride)
        {
            return resolveOverride(presetId);
        }

        if (string.IsNullOrWhiteSpace(presetId))
        {
            return null;
        }

        if (PresetExecutableCache.TryGetValue(presetId, out var cached))
        {
            if (cached is null)
            {
                return null;
            }

            if (File.Exists(cached))
            {
                return cached;
            }

            PresetExecutableCache.TryRemove(presetId, out _);
        }

        var resolved = ResolveExecutableUncached(presetId);
        PresetExecutableCache[presetId] = resolved;
        return resolved;
    }

    private static string? ResolveExecutableUncached(string presetId)
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

        if (string.Equals(presetId, PresetWebStorm, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolveWebStorm();
        }

        if (string.Equals(presetId, PresetPyCharm, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolvePyCharm();
        }

        if (string.Equals(presetId, PresetGoLand, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolveGoLand();
        }

        if (string.Equals(presetId, PresetCLion, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolveCLion();
        }

        if (string.Equals(presetId, PresetAndroidStudio, StringComparison.OrdinalIgnoreCase))
        {
            return JetBrainsInstallDiscovery.TryResolveAndroidStudio();
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

    public static bool ShouldShowBrowseRequiredPrompt(string preset, string? path) =>
        string.Equals(preset, PresetCustom, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(path);

    public static bool TryValidateFormSelection(string preset, string? path, out string error)
    {
        if (ShouldShowBrowseRequiredPrompt(preset, path))
        {
            error = BrowseRequiredMessage;
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string BuildPathWarning(string preset, string? path) =>
        ShouldShowPathWarning(preset, path)
            ? "Executable not found. Choose another app or set App preset to No companion app."
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

        if (PathExecutableLookup.TryFindOnPath(fileName, out var onPath))
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
        (PresetWebStorm, ["webstorm64.exe"]),
        (PresetPyCharm, ["pycharm64.exe"]),
        (PresetGoLand, ["goland64.exe"]),
        (PresetCLion, ["clion64.exe"]),
        (PresetAndroidStudio, ["studio64.exe", "studio.exe"]),
        (PresetZed, ["zed.exe", "Zed.exe"]),
        (PresetNotepadPlusPlus, ["notepad++.exe"]),
        (PresetVsCode, ["Code.exe"]),
        (PresetVsCodeInsiders, ["Code - Insiders.exe"]),
        (PresetCursor, ["Cursor.exe"]),
        (PresetGitKraken, ["GitKraken.exe"]),
        (PresetSourcetree, ["Sourcetree.exe"]),
        (PresetAntigravity, ["Antigravity IDE.exe", "Antigravity.exe"]),
        (PresetDevin, ["Windsurf.exe", "Devin.exe"]),
        (PresetKiro, ["Kiro.exe"]),
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

    private static IReadOnlyList<string> BuildGitKrakenCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "gitkraken", "gitkraken.exe"),
            Path.Combine(localAppData, "GitKraken", "GitKraken.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildSourcetreeCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "SourceTree", "SourceTree.exe"),
            Path.Combine(localAppData, "Atlassian", "SourceTree", "SourceTree.exe"),
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

    private static IReadOnlyList<string> BuildVsCodeInsidersCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return
        [
            Path.Combine(localAppData, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
            Path.Combine(programFiles, "Microsoft VS Code Insiders", "Code - Insiders.exe"),
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

    private static IReadOnlyList<string> BuildTraeCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Join(localAppData, "Programs", "Trae", "Trae.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildAntigravityCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(localAppData, "Programs", "Antigravity IDE", "Antigravity IDE.exe"),
            Path.Combine(localAppData, "Programs", "Antigravity", "Antigravity.exe"),
            Path.Combine(localAppData, "Programs", "antigravity", "Antigravity.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildDevinCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Devin (formerly Windsurf / Codeium IDE fork).
        return
        [
            Path.Combine(localAppData, "Programs", "Windsurf", "Windsurf.exe"),
            Path.Combine(localAppData, "Programs", "Devin", "Devin.exe"),
            Path.Combine(localAppData, "Programs", "devin", "Devin.exe"),
        ];
    }

    private static IReadOnlyList<string> BuildKiroCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return
        [
            Path.Combine(localAppData, "Programs", "Kiro", "Kiro.exe"),
            Path.Combine(programFiles, "Kiro", "Kiro.exe"),
            Path.Combine(programFilesX86, "Kiro", "Kiro.exe"),
        ];
    }
}
