using QuickShell.Interop;

namespace QuickShell.Services;

internal static class ShortcutFilePickerService
{
    private static readonly (string Name, string Spec)[] JsonFilters =
    {
        ("JSON files (*.json)", "*.json"),
        ("All files (*.*)", "*.*"),
    };

    private static readonly (string Name, string Spec)[] ExecutableFilters =
    {
        ("Applications (*.exe;*.lnk;*.bat;*.cmd)", "*.exe;*.lnk;*.bat;*.cmd"),
        ("All files (*.*)", "*.*"),
    };

    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Prompts the user to choose a destination for exporting workspace data.
    /// </summary>
    /// <param name="services">The shell services used to determine the workspace configuration directory.</param>
    /// <returns>The selected export file path, or <c>null</c> if no file is selected.</returns>
    public static string? PickExportFile(IQuickShellServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var defaultName = $"quickshell-workspaces-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var initialDirectory = DirectoryOrNull(services.Shortcuts.ConfigDirectory);
        var ownerHandle = NativeForegroundWindow.Get();

        return StaModalDialogRunner.Run(
            ownerHandle,
            () => ShellFileDialog.PickSaveFile(
                ownerHandle,
                $"Export {QuickShellBrand.DisplayName} workspaces",
                JsonFilters,
                defaultExt: "json",
                defaultFileName: defaultName,
                initialDirectory: initialDirectory),
            DialogTimeout,
            JoinGracePeriod);
    }

    /// <summary>
    /// Prompts the user to select a workspace import file.
    /// </summary>
    /// <param name="services">The shell services used to locate the shortcuts configuration directory.</param>
    /// <returns>The selected JSON file path, or <c>null</c> if no file is selected.</returns>
    public static string? PickImportFile(IQuickShellServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var initialDirectory = DirectoryOrNull(services.Shortcuts.ConfigDirectory);
        var ownerHandle = NativeForegroundWindow.Get();

        return StaModalDialogRunner.Run(
            ownerHandle,
            () => ShellFileDialog.PickOpenFile(
                ownerHandle,
                $"Import {QuickShellBrand.DisplayName} workspaces",
                JsonFilters,
                defaultExt: "json",
                initialDirectory: initialDirectory),
            DialogTimeout,
            JoinGracePeriod);
    }

    /// <summary>
    /// Prompts the user to select a companion executable file.
    /// </summary>
    /// <returns>The selected executable file path, or <c>null</c> if no file is selected.</returns>
    public static string? PickExecutableFile()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var initialDirectory = DirectoryOrNull(programFiles);
        var ownerHandle = NativeForegroundWindow.Get();

        return StaModalDialogRunner.Run(
            ownerHandle,
            () => ShellFileDialog.PickOpenFile(
                ownerHandle,
                "Choose companion app",
                ExecutableFilters,
                defaultExt: "exe",
                initialDirectory: initialDirectory),
            DialogTimeout,
            JoinGracePeriod);
    }

    /// <summary>
        /// Gets the path when it identifies an existing directory.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <returns>The existing directory path, or <c>null</c> when the path is blank or does not exist.</returns>
        private static string? DirectoryOrNull(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;
}
