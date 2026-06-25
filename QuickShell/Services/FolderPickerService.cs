using System.Runtime.InteropServices;
using System.Threading;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);

    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = GetForegroundWindow();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderOnStaThread(initialDirectory, ownerHandle);
        }

        string? selected = null;
        var thread = new Thread(() => selected = PickFolderOnStaThread(initialDirectory, ownerHandle))
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(DialogTimeout))
        {
            return null;
        }

        return selected;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = "Select a folder for this shortcut. Tab through the dialog; type a path in the address bar to jump to a folder.",
            ShowNewFolderButton = true,
            AutoUpgradeEnabled = true,
            OkRequiresInteraction = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            var initial = initialDirectory;
            if (WslPathResolver.TryParse(initialDirectory, out var wsl) && !string.IsNullOrWhiteSpace(wsl.UncPath))
            {
                initial = wsl.UncPath;
            }

            if (Directory.Exists(initial))
            {
                dialog.InitialDirectory = initial;
                dialog.SelectedPath = initial;
            }
        }

        var owner = ownerHandle != 0 ? new NativeWindowWrapper(ownerHandle) : null;
        return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private sealed class NativeWindowWrapper(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
