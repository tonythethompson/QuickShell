using QuickShell.Abstractions.Classification;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutFormLaunchSection
{
    public static List<LaunchRowDraft> CommandsFromShortcut(TerminalShortcut? shortcut, string fallbackLaunchTarget)
    {
        List<LaunchRowDraft> commands;
        if (shortcut is null)
        {
            commands = [new LaunchRowDraft { LaunchTarget = fallbackLaunchTarget, IsEditorPlaceholder = true }];
        }
        else
        {
            ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(shortcut);
            var launches = shortcut.Launches.OrderBy(entry => entry.Order).ToList();
            if (launches.Count == 0)
            {
                commands =
                [
                    new LaunchRowDraft
                    {
                        Label = shortcut.Name,
                        Command = shortcut.Command ?? string.Empty,
                        LaunchTarget = TerminalCatalog.EncodeLaunchTargetId(shortcut),
                        RunAsAdmin = shortcut.RunAsAdmin,
                        IsEnabled = true,
                    },
                ];
            }
            else
            {
                commands = LaunchRowListEditor.FromWorkspaceEntries(launches);
            }
        }

        LaunchRowListEditor.EnsureMinimumRowsForEditor(commands, fallbackLaunchTarget);
        return commands;
    }

    public static List<ShortcutFormLaunchInput> ToLaunchInputs(
        IReadOnlyList<LaunchRowDraft> commands,
        string workspaceName,
        string fallbackLaunchTarget)
    {
        var rows = LaunchRowListEditor.TrimForSave(commands);

        var labelBase = string.IsNullOrWhiteSpace(workspaceName) ? "Main" : workspaceName.Trim();
        return rows.Select((row, index) => new ShortcutFormLaunchInput
        {
            Id = row.Id,
            Label = string.IsNullOrWhiteSpace(row.Label)
                ? index == 0 ? labelBase : $"Command {index + 1}"
                : row.Label.Trim(),
            Command = row.Command,
            LaunchTarget = string.IsNullOrWhiteSpace(row.LaunchTarget)
                ? index == 0
                    ? fallbackLaunchTarget
                    : TerminalCatalog.SameAsPreviousLaunchTargetId
                : row.LaunchTarget,
            RunAsAdmin = row.RunAsAdmin,
            IsEnabled = row.IsEnabled,
            TaskType = TaskTypeCatalog.Normalize(row.TaskType),
        }).ToList();
    }

    public static string BuildCommandRowsJson(
        IReadOnlyList<LaunchRowDraft> commands,
        string terminalChoices) =>
        ShortcutLaunchFormJson.BuildCommandRowsJson(
            commands.Select(command => (command.Label, command.Command, command.TaskType, command.LaunchTarget, command.RunAsAdmin, command.IsEnabled)).ToList(),
            terminalChoices);

    public static LaunchRowDraft? TryCreateCommandFromTaskType(
        IProjectAnalysisService projectAnalysis,
        string? directory,
        string? taskType,
        IEnumerable<string?>? existingCommands = null)
    {
        ArgumentNullException.ThrowIfNull(projectAnalysis);

        var normalized = TaskTypeCatalog.Normalize(taskType);
        if (normalized == TaskTypeCatalog.None)
        {
            return null;
        }

        var pickContext = existingCommands is null
            ? TaskTypePickContext.Empty
            : TaskTypePickContext.FromCommands(existingCommands);

        var suggestion = projectAnalysis.TrySuggestTaskCommand(
            directory,
            normalized,
            pickContext);
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return null;
        }

        return new LaunchRowDraft
        {
            Command = suggestion,
            TaskType = normalized,
        };
    }
}
