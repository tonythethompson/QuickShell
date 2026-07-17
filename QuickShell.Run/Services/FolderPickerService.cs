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
            return PickFolderOnStaThread(initialDirectory, ownerHandle);
        }

        string? selected = null;
        uint staThreadId = 0;
        using var threadIdReady = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            staThreadId = GetCurrentThreadId();
            threadIdReady.Set();
            selected = PickFolderOnStaThread(initialDirectory, ownerHandle);
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        threadIdReady.Wait();

        // Happy path: PickFolderOnStaThread auto-closes at DialogTimeout and the STA thread
        // exits soon after. JoinGracePeriod covers dismiss + ShowDialog unwind. If the thread
        // is still alive, force another cross-thread dismiss, wait briefly, then cancel so the
        // caller never hangs and we do not return while leaving a live orphan dialog behind.
        if (thread.Join(DialogTimeout + JoinGracePeriod))
        {
            return selected;
        }

        DismissOpenDialogOnThread(staThreadId, ownerHandle);
        return thread.Join(JoinGracePeriod) ? selected : null;
    }

    private static string? PickFolderOnStaThread(string? initialDirectory, nint ownerHandle)
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

        // Auto-close the modal dialog if left open past the timeout so the STA thread exits
        // and repeated calls do not stack orphaned dialogs.
        using var autoClose = new System.Windows.Forms.Timer
        {
            Interval = (int)DialogTimeout.TotalMilliseconds,
        };
        autoClose.Tick += (_, _) => DismissOpenDialogOnThread(GetCurrentThreadId(), ownerHandle);
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

    private static void DismissOpenDialogOnThread(uint threadId, nint ownerHandle)
    {
        EnumThreadWindows(threadId, (hWnd, _) =>
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
