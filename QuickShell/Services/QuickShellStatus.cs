using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Threading.Tasks;

namespace QuickShell.Services;

internal static class QuickShellStatus
{
    private static readonly object ShowLock = new();
    private static StatusMessage? _activeMessage;

    public static void ShowToast(string? message, MessageState state = MessageState.Success)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (ShowLock)
        {
            if (_activeMessage is not null)
            {
                ExtensionHost.HideStatus(_activeMessage);
            }

            _activeMessage = new StatusMessage
            {
                Message = message,
                State = state,
            };

            // Page-scoped status shows on the current CmdPal page (including pinned-home navigation).
            ExtensionHost.ShowStatus(_activeMessage, StatusContext.Page);

            _ = Task.Run(async () =>
            {
                await Task.Delay(2500).ConfigureAwait(false);

                lock (ShowLock)
                {
                    if (_activeMessage is null)
                    {
                        return;
                    }

                    ExtensionHost.HideStatus(_activeMessage);
                    _activeMessage = null;
                }
            });
        }
    }
}
