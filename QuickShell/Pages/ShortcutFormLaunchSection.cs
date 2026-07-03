using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal static class ShortcutFormLaunchSection
{
    internal sealed class CommandRowDraft
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Command { get; set; } = string.Empty;
    }

    public static List<CommandRowDraft> CommandsFromShortcut(TerminalShortcut? shortcut)
    {
        if (shortcut is null)
        {
            return [new CommandRowDraft()];
        }

        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
        var launches = shortcut.Launches.OrderBy(entry => entry.Order).ToList();
        if (launches.Count == 0)
        {
            return
            [
                new CommandRowDraft
                {
                    Command = shortcut.Command ?? string.Empty,
                },
            ];
        }

        return launches
            .Select(entry => new CommandRowDraft
            {
                Id = entry.Id,
                Command = entry.Command ?? string.Empty,
            })
            .ToList();
    }

    public static List<ShortcutFormLaunchInput> ToLaunchInputs(
        IReadOnlyList<CommandRowDraft> commands,
        string workspaceName,
        string launchTarget,
        bool runAsAdmin)
    {
        var rows = commands.ToList();
        while (rows.Count > 1 && string.IsNullOrWhiteSpace(rows[^1].Command))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        if (rows.Count == 0)
        {
            rows.Add(new CommandRowDraft());
        }

        var labelBase = string.IsNullOrWhiteSpace(workspaceName) ? "Main" : workspaceName.Trim();
        return rows.Select((row, index) => new ShortcutFormLaunchInput
        {
            Id = row.Id,
            Label = index == 0 ? labelBase : $"Command {index + 1}",
            Command = row.Command,
            LaunchTarget = launchTarget,
            RunAsAdmin = runAsAdmin,
            IsEnabled = true,
        }).ToList();
    }

    public static string BuildCommandRowsJson(IReadOnlyList<CommandRowDraft> commands) =>
        ShortcutLaunchFormJson.BuildCommandRowsJson(
            commands.Select(command => command.Command).ToList());
}
