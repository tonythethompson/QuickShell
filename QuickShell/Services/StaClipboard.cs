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

    /// <summary>
    /// Attempts to place text on the Windows clipboard.
    /// </summary>
    /// <param name="text">The text to place on the clipboard.</param>
    /// <returns><c>true</c> if the text was placed successfully within the timeout; <c>false</c> otherwise.</returns>
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
    /// <summary>
/// Attempts to place text on the Windows clipboard.
/// </summary>
/// <returns><c>true</c> if the text was set successfully; <c>false</c> otherwise.</returns>
    private static bool SetTextOnStaThread(string text) => Win32Clipboard.SetText(text);

    /// <summary>
/// Reads text from the Windows clipboard on an STA thread.
/// </summary>
/// <returns>The clipboard text, or <see langword="null"/> if no text is available.</returns>
private static string? ReadTextOnStaThread() => Win32Clipboard.GetText();
}
