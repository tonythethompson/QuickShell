using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

internal static class StaClipboard
{
    public static bool TrySetText(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return SetText();
        var success = false;
        var thread = new Thread(() => success = SetText()) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(5)) && success;

        // Win32Clipboard returns false on failure; no generic catch needed.
        bool SetText() => Win32Clipboard.SetText(text);
    }
}
