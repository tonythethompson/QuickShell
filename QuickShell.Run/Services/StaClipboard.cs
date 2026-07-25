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

        bool SetText()
        {
            try { return Win32Clipboard.SetText(text); }
            catch { return false; }
        }
    }
}
