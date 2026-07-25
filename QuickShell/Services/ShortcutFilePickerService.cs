using System.Threading;
using System.Runtime.InteropServices;
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

    public static string? PickExportFile(IQuickShellServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var defaultName = $"quickshell-workspaces-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var initialDirectory = DirectoryOrNull(services.Shortcuts.ConfigDirectory);

        return RunOnStaThread(() => ShellFileDialog.PickSaveFile(
            GetForegroundWindow(),
            $"Export {QuickShellBrand.DisplayName} workspaces",
            JsonFilters,
            defaultExt: "json",
            defaultFileName: defaultName,
            initialDirectory: initialDirectory));
    }

    public static string? PickImportFile(IQuickShellServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var initialDirectory = DirectoryOrNull(services.Shortcuts.ConfigDirectory);

        return RunOnStaThread(() => ShellFileDialog.PickOpenFile(
            GetForegroundWindow(),
            $"Import {QuickShellBrand.DisplayName} workspaces",
            JsonFilters,
            defaultExt: "json",
            initialDirectory: initialDirectory));
    }

    public static string? PickExecutableFile()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var initialDirectory = DirectoryOrNull(programFiles);

        return RunOnStaThread(() => ShellFileDialog.PickOpenFile(
            GetForegroundWindow(),
            "Choose companion app",
            ExecutableFilters,
            defaultExt: "exe",
            initialDirectory: initialDirectory));
    }

    private static string? DirectoryOrNull(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;

    private static string? RunOnStaThread(Func<string?> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return action();
        }

        string? result = null;
        var thread = new Thread(() => result = action())
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return thread.Join(DialogTimeout) ? result : null;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
