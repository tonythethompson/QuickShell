using System.Threading;
using QuickShell.Interop;

namespace QuickShell.Services;

internal static class StaClipboard
{
    /// <summary>
    /// Attempts to set the clipboard text, using an STA thread when required.
    /// </summary>
    /// <param name="text">The text to place on the clipboard.</param>
    /// <returns><c>true</c> if the clipboard text is set successfully within five seconds; <c>false</c> otherwise.</returns>
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
