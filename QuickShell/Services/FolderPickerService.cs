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

        // Wait for the dialog thread to exit. PickFolderOnStaThread auto-closes the modal
        // dialog via a timer at DialogTimeout, so this returns at most DialogTimeout after the
        // dialog opens and never leaves an orphaned native dialog behind (which would otherwise
        // let repeated calls stack up open dialogs).
        thread.Join();
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

        // Auto-close the modal dialog if it is left open past the timeout. Without this the
        // background STA thread keeps the native dialog alive after the caller gives up, and
        // repeated calls stack orphaned dialogs. The Timer ticks on this thread's ShowDialog
        // message loop; WM_CLOSE dismisses the dialog and lets the thread exit.
        using var autoClose = new System.Windows.Forms.Timer
        {
            Interval = (int)DialogTimeout.TotalMilliseconds,
        };
        autoClose.Tick += (_, _) => DismissOpenDialog(ownerHandle);
        autoClose.Start();

        var owner = ownerHandle != 0 ? new NativeWindowWrapper(ownerHandle) : null;
        var result = dialog.ShowDialog(owner);
        autoClose.Stop();
        return result == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private const int WM_CLOSE = 0x0010;

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadWindowsProc lpfn, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    private delegate bool EnumThreadWindowsProc(nint hWnd, nint lParam);

    /// <summary>
    /// Posts <see cref="WM_CLOSE"/> to the modal dialog window owned by this thread (the
    /// <paramref name="ownerHandle"/> window is skipped). Used by the auto-close timer so the
    /// native folder dialog is actually dismissed at the timeout instead of being abandoned.
    /// </summary>
    private static void DismissOpenDialog(nint ownerHandle)
    {
        EnumThreadWindows(GetCurrentThreadId(), (hWnd, _) =>
        {
            if (hWnd != ownerHandle)
            {
                PostMessage(hWnd, WM_CLOSE, nint.Zero, nint.Zero);
            }

            return true;
        }, nint.Zero);
    }

    private sealed class NativeWindowWrapper(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
