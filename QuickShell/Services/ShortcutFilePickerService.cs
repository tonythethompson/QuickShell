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

    private static string? DirectoryOrNull(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;
}
