using QuickShell.Models;
using System.ComponentModel;

namespace QuickShell.Services;

internal readonly record struct ShortcutLaunchOptions(
    bool RunAsAdmin = false,
    bool RunAsStandard = false,
    bool IncludeCompanionApp = true,
    bool IncludeDevServerLink = true);

internal sealed class ShortcutLaunchResult
{
    public bool Dismiss { get; init; }

    public string? StayOpenMessage { get; init; }

    public bool MarkUsed { get; init; }

    public static ShortcutLaunchResult Dismissed(bool markUsed = true) =>
        new() { Dismiss = true, MarkUsed = markUsed };

    public static ShortcutLaunchResult StayOpen(string message, bool markUsed = false) =>
        new() { Dismiss = false, StayOpenMessage = message, MarkUsed = markUsed };
}

internal static class ShortcutLaunchExecutor
{
    public static ShortcutLaunchResult Launch(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);

        if (!TryGetLaunchDirectory(shortcut.Directory, out _, out var directoryError))
        {
            return ShortcutLaunchResult.StayOpen(directoryError);
        }

        var enabledLaunches = ShortcutLaunchNormalization.GetEnabledLaunches(shortcut);
        if (enabledLaunches.Count == 0)
        {
            return ShortcutLaunchResult.StayOpen("Workspace has no enabled launch entries.");
        }

        var companionAttempted = options.IncludeCompanionApp
            && CompanionAppLauncher.ShouldLaunchOnWorkspaceOpen(shortcut);
        string? companionError = null;
        var companionSucceeded = !companionAttempted
            || CompanionAppLauncher.TryLaunch(shortcut, onDemand: false, out companionError);

        if (enabledLaunches.Count == 1)
        {
            return LaunchSingle(
                shortcut,
                enabledLaunches[0],
                terminalApplicationId,
                defaultProfileId,
                options,
                companionAttempted,
                companionSucceeded,
                companionError);
        }

        return LaunchAll(
            shortcut,
            enabledLaunches,
            terminalApplicationId,
            defaultProfileId,
            options,
            companionAttempted,
            companionSucceeded,
            companionError);
    }

    public static ShortcutLaunchResult LaunchEntry(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options = default)
    {
        if (!TryGetLaunchDirectory(shortcut.Directory, out _, out var directoryError))
        {
            return ShortcutLaunchResult.StayOpen(directoryError);
        }

        return LaunchSingle(
            shortcut,
            launch,
            terminalApplicationId,
            defaultProfileId,
            options,
            companionAttempted: false,
            companionSucceeded: true,
            companionError: null);
    }

    private static ShortcutLaunchResult LaunchSingle(
        TerminalShortcut shortcut,
        WorkspaceEntry launch,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        bool companionSucceeded,
        string? companionError)
    {
        try
        {
            var launchShortcut = ShortcutLaunchNormalization.ToLaunchShortcut(launch, shortcut);
            TerminalLauncher.Open(
                launchShortcut,
                terminalApplicationId,
                defaultProfileId,
                options.RunAsAdmin,
                options.RunAsStandard);

            return BuildPostLaunchResult(
                shortcut,
                options,
                companionAttempted,
                companionSucceeded,
                companionError,
                "Workspace opened");
        }
        catch (DirectoryNotFoundException)
        {
            return ShortcutLaunchResult.StayOpen(
                "Failed to open terminal: the folder path could not be found.");
        }
        catch (InvalidOperationException)
        {
            return ShortcutLaunchResult.StayOpen(
                "Failed to open terminal: check the workspace settings and try again.");
        }
        catch (Win32Exception)
        {
            return ShortcutLaunchResult.StayOpen(
                "Failed to open terminal: launch was canceled or blocked by the system.");
        }
    }

    private static ShortcutLaunchResult LaunchAll(
        TerminalShortcut shortcut,
        IReadOnlyList<WorkspaceEntry> enabledLaunches,
        string terminalApplicationId,
        string defaultProfileId,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        bool companionSucceeded,
        string? companionError)
    {
        var opened = 0;
        string? lastFailureLabel = null;

        foreach (var launch in enabledLaunches)
        {
            try
            {
                var launchShortcut = ShortcutLaunchNormalization.ToLaunchShortcut(launch, shortcut);
                TerminalLauncher.Open(
                    launchShortcut,
                    terminalApplicationId,
                    defaultProfileId,
                    options.RunAsAdmin,
                    options.RunAsStandard);
                opened++;
            }
            catch (DirectoryNotFoundException)
            {
                lastFailureLabel = launch.Label;
            }
            catch (InvalidOperationException)
            {
                lastFailureLabel = launch.Label;
            }
            catch (Win32Exception)
            {
                lastFailureLabel = launch.Label;
            }
        }

        if (opened == 0)
        {
            return ShortcutLaunchResult.StayOpen(
                lastFailureLabel is null
                    ? "Workspace could not launch any terminals."
                    : $"{lastFailureLabel} could not be launched.");
        }

        var successPrefix = opened == enabledLaunches.Count
            ? "Workspace launched"
            : $"Workspace partially launched: {opened} of {enabledLaunches.Count} terminals opened";

        return BuildPostLaunchResult(
            shortcut,
            options,
            companionAttempted,
            companionSucceeded,
            companionError,
            successPrefix,
            partialLaunch: opened < enabledLaunches.Count);
    }

    private static ShortcutLaunchResult BuildPostLaunchResult(
        TerminalShortcut shortcut,
        ShortcutLaunchOptions options,
        bool companionAttempted,
        bool companionSucceeded,
        string? companionError,
        string successPrefix,
        bool partialLaunch = false)
    {
        var warnings = new List<string>();

        if (companionAttempted && !companionSucceeded)
        {
            warnings.Add(FormatLaunchWarning("Companion app could not be launched.", companionError));
        }

        if (options.IncludeDevServerLink
            && WorkspaceDevServerActions.ShouldOpenOnWorkspaceLaunch(shortcut)
            && !WorkspaceDevServerActions.TryOpen(shortcut, out var devServerError))
        {
            warnings.Add(FormatLaunchWarning("Dev server link could not be opened.", devServerError));
        }

        if (warnings.Count == 0 && !partialLaunch)
        {
            return ShortcutLaunchResult.Dismissed();
        }

        if (warnings.Count == 0)
        {
            return ShortcutLaunchResult.StayOpen($"{successPrefix}.", markUsed: true);
        }

        return ShortcutLaunchResult.StayOpen(
            $"{successPrefix}, but {string.Join(" ", warnings)}",
            markUsed: true);
    }

    private static bool TryGetLaunchDirectory(string? directory, out string normalizedDirectory, out string error)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory ?? string.Empty, out normalizedDirectory, out error))
        {
            error = $"Workspace could not launch: {error}";
            return false;
        }

        if (!ShortcutValidation.DirectoryExists(normalizedDirectory))
        {
            error = $"Workspace could not launch: folder not found at {normalizedDirectory}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string FormatLaunchWarning(string summary, string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? summary
            : $"{summary} {detail}";
}
