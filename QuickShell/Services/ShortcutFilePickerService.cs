using System.Runtime.InteropServices;
using System.Threading;
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
        var ownerHandle = GetForegroundWindow();

        return RunOnStaThread(
            ownerHandle,
            () => ShellFileDialog.PickSaveFile(
                ownerHandle,
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
        var ownerHandle = GetForegroundWindow();

        return RunOnStaThread(
            ownerHandle,
            () => ShellFileDialog.PickOpenFile(
                ownerHandle,
                $"Import {QuickShellBrand.DisplayName} workspaces",
                JsonFilters,
                defaultExt: "json",
                initialDirectory: initialDirectory));
    }

    public static string? PickExecutableFile()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var initialDirectory = DirectoryOrNull(programFiles);
        var ownerHandle = GetForegroundWindow();

        return RunOnStaThread(
            ownerHandle,
            () => ShellFileDialog.PickOpenFile(
                ownerHandle,
                "Choose companion app",
                ExecutableFilters,
                defaultExt: "exe",
                initialDirectory: initialDirectory));
    }

    private static string? DirectoryOrNull(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;

    private static string? RunOnStaThread(nint ownerHandle, Func<string?> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return action();
        }

        string? result = null;
        var nativeThreadId = 0;
        var thread = new Thread(() =>
        {
            nativeThreadId = StaDialogCloser.GetCurrentThreadId();
            result = action();
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (thread.Join(DialogTimeout + JoinGracePeriod))
        {
            return result;
        }

        StaDialogCloser.TryCloseThreadDialog(Volatile.Read(ref nativeThreadId), ownerHandle);
        return thread.Join(JoinGracePeriod) ? result : null;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
