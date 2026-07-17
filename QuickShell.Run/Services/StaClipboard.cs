using System.Threading;
using System.Windows.Forms;

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
            try { Clipboard.SetText(text); return true; }
            catch { return false; }
        }
    }
}
