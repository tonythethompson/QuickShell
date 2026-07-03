using System.Runtime.InteropServices;
using System.Threading;

namespace QuickShell.Services;

internal static class ShortcutFilePickerService
{
    private const string JsonFilter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);

    public static string? PickExportFile()
    {
        var defaultName = $"quickshell-workspaces-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var initialDirectory = QuickShellRuntimeServices.Shortcuts.ConfigDirectory;

        return RunOnStaThread(() =>
        {
            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = $"Export {QuickShellBrand.DisplayName} workspaces",
                Filter = JsonFilter,
                DefaultExt = "json",
                AddExtension = true,
                FileName = defaultName,
                OverwritePrompt = true,
            };

            if (Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return ShowDialog(dialog);
        });
    }

    public static string? PickImportFile()
    {
        var initialDirectory = QuickShellRuntimeServices.Shortcuts.ConfigDirectory;

        return RunOnStaThread(() =>
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = $"Import {QuickShellBrand.DisplayName} workspaces",
                Filter = JsonFilter,
                DefaultExt = "json",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return ShowDialog(dialog);
        });
    }

    public static string? PickImportWorkspacesFile()
    {
        var initialDirectory = QuickShellRuntimeServices.Shortcuts.ConfigDirectory;

        return RunOnStaThread(() =>
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = $"Import {QuickShellBrand.DisplayName} workspaces",
                Filter = JsonFilter,
                DefaultExt = "json",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return ShowDialog(dialog);
        });
    }

    public static string? PickExecutableFile()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var initialDirectory = Directory.Exists(programFiles) ? programFiles : null;

        return RunOnStaThread(() =>
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Choose companion app",
                Filter = "Applications (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*",
                DefaultExt = "exe",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (initialDirectory is not null)
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return ShowDialog(dialog);
        });
    }

    public static string? PickExportWorkspacesFile()
    {
        var defaultName = $"quickshell-workspaces-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var initialDirectory = QuickShellRuntimeServices.Shortcuts.ConfigDirectory;

        return RunOnStaThread(() =>
        {
            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = $"Export {QuickShellBrand.DisplayName} workspaces",
                Filter = JsonFilter,
                DefaultExt = "json",
                AddExtension = true,
                FileName = defaultName,
                OverwritePrompt = true,
            };

            if (Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return ShowDialog(dialog);
        });
    }

    private static string? ShowDialog(System.Windows.Forms.FileDialog dialog)
    {
        var ownerHandle = GetForegroundWindow();
        var owner = ownerHandle != 0 ? new NativeWindowWrapper(ownerHandle) : null;
        return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK
            ? dialog.FileName
            : null;
    }

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

    private sealed class NativeWindowWrapper(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
