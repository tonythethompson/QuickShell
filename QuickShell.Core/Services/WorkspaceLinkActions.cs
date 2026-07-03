using System.Diagnostics;

namespace QuickShell.Services;

internal static class WorkspaceLinkActions
{
    public static bool TryOpenLink(string? url, out string error)
    {
        if (!ShortcutValidation.TryValidateOptionalLinkUrl(url, out error, out var normalized))
        {
            return false;
        }

        if (normalized is null)
        {
            error = "Link is not configured.";
            return false;
        }

        if (Process.Start(new ProcessStartInfo
            {
                FileName = normalized,
                UseShellExecute = true,
            }) is null)
        {
            error = "Failed to open link.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
