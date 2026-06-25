using System.Threading;
using System.Windows.Forms;

namespace QuickShell.Services;

internal static class StaClipboard
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    public static string? TryReadText()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return ReadTextOnStaThread();
        }

        string? text = null;
        var thread = new Thread(() => text = ReadTextOnStaThread())
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return thread.Join(ReadTimeout) ? text : null;
    }

    private static string? ReadTextOnStaThread() =>
        Clipboard.ContainsText() ? Clipboard.GetText() : null;
}
