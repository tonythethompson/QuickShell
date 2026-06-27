using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell;

internal static class QuickShellNavigation
{
    public const string HomePageId = "com.quickshell.home";

    public static CommandResult ReturnHome(string? toastMessage = null)
    {
        AbandonPendingImportIfLeavingSettings(ref toastMessage);
        ShowToast(toastMessage);
        return CommandResult.GoToPage(new GoToPageArgs
        {
            PageId = HomePageId,
        });
    }

    public static CommandResult ReturnToShortcutsList(string? toastMessage = null) => ReturnHome(toastMessage);

    public static CommandResult StayOpen(string? toastMessage = null)
    {
        ShowToast(toastMessage);
        return CommandResult.KeepOpen();
    }

    public static CommandResult GoBack(string? toastMessage = null)
    {
        ShowToast(toastMessage);
        return CommandResult.GoBack();
    }

    public static CommandResult PopToShortcutsList(string? toastMessage = null) => GoBack(toastMessage);

    public static CommandResult GoToSettings(string? toastMessage = null)
    {
        ShowToast(toastMessage);
        return CommandResult.GoToPage(new GoToPageArgs
        {
            PageId = Pages.QuickShellExtensionSettingsPage.PageId,
        });
    }

    public static CommandResult GoToCreateShortcut(string? toastMessage = null)
    {
        AbandonPendingImportIfLeavingSettings(ref toastMessage);
        ShowToast(toastMessage);
        return CommandResult.GoToPage(new GoToPageArgs
        {
            PageId = Services.ShortcutCommandIds.CreateShortcut,
        });
    }

    public static CommandResult StayOnSettings(string? toastMessage = null) => StayOpen(toastMessage);

    private static void AbandonPendingImportIfLeavingSettings(ref string? statusMessage)
    {
        if (!ImportConflictState.TryAbandonPending(out var abandonMessage))
        {
            return;
        }

        statusMessage = string.IsNullOrWhiteSpace(statusMessage)
            ? abandonMessage
            : $"{statusMessage} {abandonMessage}";
    }

    private static void ShowToast(string? toastMessage)
    {
        QuickShellStatus.ShowToast(toastMessage);
    }
}
