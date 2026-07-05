using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Pages;

internal static class ShortcutFormLaunchSection
{
    internal sealed class CommandRowDraft
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Command { get; set; } = string.Empty;

        public string TaskType { get; set; } = TaskTypeCatalog.None;

        public string LaunchTarget { get; set; } = "default";
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
                    LaunchTarget = TerminalCatalog.EncodeLaunchTargetId(shortcut),
                },
            ];
        }

        return launches
            .Select(entry => new CommandRowDraft
            {
                Id = entry.Id,
                Command = entry.Command ?? string.Empty,
                TaskType = TaskTypeCatalog.Normalize(entry.TaskType),
                LaunchTarget = ShortcutFormSave.EncodeLaunchTargetForEntry(entry),
            })
            .ToList();
    }

    public static List<ShortcutFormLaunchInput> ToLaunchInputs(
        IReadOnlyList<CommandRowDraft> commands,
        string workspaceName,
        string fallbackLaunchTarget,
        bool runAsAdmin)
    {
        var rows = commands.ToList();
        while (rows.Count > 1
            && string.IsNullOrWhiteSpace(rows[^1].Command)
            && string.Equals(TaskTypeCatalog.Normalize(rows[^1].TaskType), TaskTypeCatalog.None, StringComparison.Ordinal))
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
            LaunchTarget = string.IsNullOrWhiteSpace(row.LaunchTarget)
                ? index == 0
                    ? fallbackLaunchTarget
                    : TerminalCatalog.SameAsPreviousLaunchTargetId
                : row.LaunchTarget,
            RunAsAdmin = runAsAdmin,
            IsEnabled = true,
            TaskType = TaskTypeCatalog.Normalize(row.TaskType),
        }).ToList();
    }

    public static string BuildCommandRowsJson(
        IReadOnlyList<CommandRowDraft> commands,
        string terminalChoices) =>
        ShortcutLaunchFormJson.BuildCommandRowsJson(
            commands.Select(command => (command.Command, command.TaskType, command.LaunchTarget)).ToList(),
            terminalChoices);

    public static CommandRowDraft? TryCreateCommandFromTaskType(
        string? directory,
        string? taskType,
        IEnumerable<string?>? existingCommands = null)
    {
        var normalized = TaskTypeCatalog.Normalize(taskType);
        if (normalized == TaskTypeCatalog.None)
        {
            return null;
        }

        var pickContext = existingCommands is null
            ? TaskTypePickContext.Empty
            : TaskTypePickContext.FromCommands(existingCommands);

        return new CommandRowDraft
        {
            Command = TaskTypeCommandSuggestion.TrySuggest(directory, normalized, pickContext) ?? string.Empty,
            TaskType = normalized,
        };
    }
}
