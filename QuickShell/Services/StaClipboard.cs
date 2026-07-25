using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

internal static class StaClipboard
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    public static string? TryReadText()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return ReadTextOnStaThread();
        string? text = null;
        var thread = new Thread(() => text = ReadTextOnStaThread()) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(ReadTimeout) ? text : null;
    }

    public static bool TrySetText(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return SetTextOnStaThread(text);
        var success = false;
        var thread = new Thread(() => success = SetTextOnStaThread(text)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(ReadTimeout) && success;
    }

    // Win32Clipboard returns false/null on failure and does not throw for the clipboard
    // failure modes we care about (open contention, empty format, alloc failure).
    private static bool SetTextOnStaThread(string text) => Win32Clipboard.SetText(text);

    private static string? ReadTextOnStaThread() => Win32Clipboard.GetText();
}
