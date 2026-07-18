using System.Runtime.InteropServices;
using System.Threading;

namespace QuickShell.Services;

/// <summary>
/// Packaged Win32 COM hosts often sit forever on WaitOne with no visible window.
/// During Store update/uninstall, Windows only delivers WM_CLOSE / end-session messages
/// when the process has a window; otherwise it force-kills and buckets HANG_QUIESCE.
/// This watcher owns a message-only HWND on a dedicated pump thread and signals exit.
/// </summary>
internal sealed partial class PackageServicingShutdownWatcher : IDisposable
{
    private const int WmDestroy = 0x0002;
    private const int WmClose = 0x0010;
    private const int WmQuit = 0x0012;
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly object RegisterSync = new();
    private static bool _classRegistered;

    private readonly ManualResetEvent _exitSignal;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly WndProcDelegate _wndProc;
    private volatile IntPtr _hwnd = IntPtr.Zero;
    private bool _disposed;

    private PackageServicingShutdownWatcher(ManualResetEvent exitSignal)
    {
        _exitSignal = exitSignal;
        // Keep the native window procedure rooted for the process lifetime of this watcher.
        _wndProc = WndProc;
        _thread = new Thread(RunMessagePump)
        {
            IsBackground = true,
            Name = "QuickShell.PackageServicingShutdown",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            // Keep waiting on COM lifetime; shutdown signals may be unavailable.
            SupportDiagnostics.Write("PackageServicingShutdownWatcher", "start-timeout");
        }
    }

    public static PackageServicingShutdownWatcher Start(ManualResetEvent exitSignal)
    {
        ArgumentNullException.ThrowIfNull(exitSignal);
        return new PackageServicingShutdownWatcher(exitSignal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var hwnd = _hwnd;
        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // Message-pump threads can't be safely force-terminated; if the pump
            // hasn't exited we let it run and the process exits with the COM host.
            SupportDiagnostics.Write("PackageServicingShutdownWatcher", "join-timeout");

            var currentHwnd = _hwnd;
            if (currentHwnd != IntPtr.Zero)
            {
                var threadId = GetWindowThreadProcessId(currentHwnd, out _);
                if (threadId != 0 && PostThreadMessage(threadId, (uint)WmQuit, IntPtr.Zero, IntPtr.Zero))
                {
                    if (!_thread.Join(TimeSpan.FromSeconds(1)))
                    {
                        SupportDiagnostics.Write("PackageServicingShutdownWatcher", "join-timeout-after-quit");
                    }
                }
            }
        }

        _started.Dispose();
    }

    private void RunMessagePump()
    {
        try
        {
            EnsureWindowClass();
            // Hidden top-level tool window (not HWND_MESSAGE): Store packaging counts
            // WindowNum and delivers WM_CLOSE during update/uninstall only when a real
            // window exists. Never ShowWindow — this stays invisible.
            _hwnd = CreateWindowEx(
                dwExStyle: WsExNoActivate | WsExToolWindow,
                lpClassName: "QuickShell.PackageServicingShutdown",
                lpWindowName: "QuickShell Package Servicing",
                dwStyle: WsPopup,
                x: 0,
                y: 0,
                nWidth: 0,
                nHeight: 0,
                hWndParent: IntPtr.Zero,
                hMenu: IntPtr.Zero,
                hInstance: GetModuleHandle(null),
                lpParam: IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                SupportDiagnostics.Write(
                    "PackageServicingShutdownWatcher",
                    "create-window-failed",
                    new { error = Marshal.GetLastWin32Error() });
                _started.Set();
                return;
            }

            SupportDiagnostics.Write(
                "PackageServicingShutdownWatcher",
                "listening",
                new { hwnd = _hwnd.ToInt64() });

            _started.Set();

            while (true)
            {
                var result = GetMessage(out var msg, IntPtr.Zero, 0, 0);
                if (result > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                else if (result == 0)
                {
                    break;
                }
                else
                {
                    SupportDiagnostics.Write(
                        "PackageServicingShutdownWatcher",
                        "getmessage-failed",
                        new { hwnd = _hwnd.ToInt64(), errorCode = Marshal.GetLastWin32Error() });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            SupportDiagnostics.WriteException("PackageServicingShutdownWatcher", ex);
            _started.Set();
        }
        finally
        {
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmQueryEndSession:
                // Allow the system to continue ending the session / updating the package.
                return new IntPtr(1);

            case WmEndSession:
                if (wParam != IntPtr.Zero)
                {
                    SignalExit("WM_ENDSESSION");
                }

                return IntPtr.Zero;

            case WmClose:
                SignalExit("WM_CLOSE");
                DestroyWindow(hwnd);
                return IntPtr.Zero;

            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private void SignalExit(string reason)
    {
        SupportDiagnostics.Write("PackageServicingShutdownWatcher", "signal-exit", new { reason });

        try
        {
            _exitSignal.Set();
        }
        catch (ObjectDisposedException)
        {
            // Lifetime already ended.
        }
    }

    private void EnsureWindowClass()
    {
        lock (RegisterSync)
        {
            if (_classRegistered)
            {
                return;
            }

            var wndClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = "QuickShell.PackageServicingShutdown",
            };

            var atom = RegisterClassEx(ref wndClass);
            if (atom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorClassAlreadyExists)
                {
                    throw new InvalidOperationException($"RegisterClassEx failed: {error}");
                }
            }

            _classRegistered = true;
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
