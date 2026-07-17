using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickShell.Services;

internal static class FolderPickerService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinGracePeriod = TimeSpan.FromSeconds(5);

    public static string? PickFolder(string? initialDirectory = null)
    {
        var ownerHandle = GetForegroundWindow();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderOnStaThread(initialDirectory, ownerHandle, dialogHwnd: null);
        }

        string? selected = null;
        var dialogHwnd = new DialogHwndBox();
        var thread = new Thread(() => selected = PickFolderOnStaThread(initialDirectory, ownerHandle, dialogHwnd))
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Happy path: PickFolderOnStaThread auto-closes at DialogTimeout and the STA thread
        // exits soon after. JoinGracePeriod covers dismiss + ShowDialog unwind. If the thread
        // is still alive, force another WM_CLOSE to the captured dialog hwnd, wait briefly,
        // then cancel so the caller never hangs and we do not leave a live orphan dialog behind.
        if (thread.Join(DialogTimeout + JoinGracePeriod))
        {
            return selected;
        }

        TryCloseDialog(dialogHwnd.Value, ownerHandle);
        return thread.Join(JoinGracePeriod) ? selected : null;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle, DialogHwndBox? dialogHwnd)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = "Select a folder for this shortcut.",
            ShowNewFolderButton = true,
            AutoUpgradeEnabled = true,
            OkRequiresInteraction = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
            dialog.SelectedPath = initialDirectory;
        }

        // Capture the dialog hwnd once it becomes foreground (same STA message loop as
        // ShowDialog) so auto-close and the caller-side safety net can PostMessage WM_CLOSE
        // without EnumThreadWindows / GetCurrentThreadId.
        using var captureHwnd = new System.Windows.Forms.Timer
        {
            Interval = 50,
        };
        captureHwnd.Tick += (_, _) =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == 0 || hwnd == ownerHandle)
            {
                return;
            }

            if (dialogHwnd is not null)
            {
                dialogHwnd.Value = hwnd;
            }

            captureHwnd.Stop();
        };

        // Auto-close the modal dialog if left open past the timeout so the STA thread exits
        // and repeated calls do not stack orphaned dialogs.
        using var autoClose = new System.Windows.Forms.Timer
        {
            Interval = (int)DialogTimeout.TotalMilliseconds,
        };
        autoClose.Tick += (_, _) =>
        {
            autoClose.Stop();
            var captured = dialogHwnd?.Value ?? 0;
            TryCloseDialog(captured != 0 ? captured : GetForegroundWindow(), ownerHandle);
        };

        captureHwnd.Start();
        autoClose.Start();

        var owner = ownerHandle != 0 ? new NativeWindowWrapper(ownerHandle) : null;
        var result = dialog.ShowDialog(owner);
        captureHwnd.Stop();
        autoClose.Stop();
        return result == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private static void TryCloseDialog(nint hwnd, nint ownerHandle)
    {
        if (hwnd == 0 || hwnd == ownerHandle)
        {
            return;
        }

        PostMessage(hwnd, WM_CLOSE, nint.Zero, nint.Zero);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private const int WM_CLOSE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    private sealed class DialogHwndBox
    {
        private nint _value;

        public nint Value
        {
            get => Volatile.Read(ref _value);
            set => Volatile.Write(ref _value, value);
        }
    }

    private sealed class NativeWindowWrapper(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}