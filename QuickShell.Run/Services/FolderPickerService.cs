using System.Runtime.InteropServices;
using System.Threading;
using System.IO;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);

    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = GetForegroundWindow();
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return PickFolderOnStaThread(initialDirectory, ownerHandle);
        string? selected = null;
        var thread = new Thread(() => selected = PickFolderOnStaThread(initialDirectory, ownerHandle)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(DialogTimeout) ? selected : null;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Select a folder for this shortcut.", ShowNewFolderButton = true, AutoUpgradeEnabled = true, OkRequiresInteraction = false };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
            dialog.SelectedPath = initialDirectory;
        }
        var owner = ownerHandle != 0 ? new NativeWindowWrapper(ownerHandle) : null;
        return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private sealed class NativeWindowWrapper(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
