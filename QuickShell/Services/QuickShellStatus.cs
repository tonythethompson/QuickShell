using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

internal static class QuickShellStatus
{
    public static void ShowToast(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        // Toolkit toasts survive settings form rebuilds. ExtensionHost.ShowStatus(Page)
        // was cleared when adaptive cards called RaiseItemsChanged after save/import.
        new ToastStatusMessage(message).Show();
    }
}
