using QuickShell.Models;
using System.Diagnostics;

namespace QuickShell.Services;

internal static class CompanionAppLauncher
{
    public static bool IsConfigured(TerminalShortcut shortcut) =>
        !string.IsNullOrWhiteSpace(shortcut.CompanionAppPath);

    public static bool ShouldLaunchOnWorkspaceOpen(TerminalShortcut shortcut) =>
        shortcut.OpenCompanionAppOnLaunch && IsConfigured(shortcut);

    public static bool TryLaunch(TerminalShortcut shortcut, bool onDemand, out string? error)
    {
        error = null;

        if (!onDemand && !ShouldLaunchOnWorkspaceOpen(shortcut))
        {
            return true;
        }

        if (!IsConfigured(shortcut))
        {
            error = onDemand
                ? "No companion app is configured for this workspace."
                : "Companion app launch is enabled but no executable is configured.";
            return false;
        }

        if (!CompanionAppCatalog.TryResolveExecutablePath(shortcut.CompanionAppPath, out var executablePath))
        {
            error = $"Companion app not found: {shortcut.CompanionAppPath}";
            return false;
        }

        if (!Directory.Exists(shortcut.Directory))
        {
            error = $"Workspace folder not found: {shortcut.Directory}";
            return false;
        }

        try
        {
            var arguments = ExpandArguments(shortcut.CompanionAppArguments, shortcut.Directory);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = shortcut.Directory,
                UseShellExecute = true,
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = "Companion app could not be launched.";
            return false;
        }
    }

    internal static string ExpandArguments(string? arguments, string workspaceDirectory)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        var trimmed = arguments.Trim();
        if (string.Equals(trimmed, ".", StringComparison.Ordinal))
        {
            return QuoteIfNeeded(workspaceDirectory);
        }

        var solution = WorkspaceCompanionSignals.TryFindSolutionFile(workspaceDirectory);
        var expanded = trimmed
            .Replace("{folder}", QuoteIfNeeded(workspaceDirectory), StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{solution}",
                QuoteIfNeeded(solution ?? workspaceDirectory),
                StringComparison.OrdinalIgnoreCase);

        return expanded;
    }

    private static string QuoteIfNeeded(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
