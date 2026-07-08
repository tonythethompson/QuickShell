using QuickShell.Models;

namespace QuickShell.Services;

internal static class LaunchRowListEditor
{
    public const int MinimumEditorRowCount = 3;

    public static List<LaunchRowDraft> FromWorkspaceEntries(IEnumerable<WorkspaceEntry> entries) =>
        entries
            .OrderBy(entry => entry.Order)
            .Select(entry => new LaunchRowDraft
            {
                Id = entry.Id,
                Command = entry.Command ?? string.Empty,
                TaskType = TaskTypeCatalog.Normalize(entry.TaskType),
                LaunchTarget = ShortcutFormSave.EncodeLaunchTargetForEntry(entry),
            })
            .ToList();

    public static void EnsureMinimumRows(List<LaunchRowDraft> rows, string fallbackLaunchTarget)
    {
        if (rows.Count >= MinimumEditorRowCount)
        {
            return;
        }

        while (rows.Count < MinimumEditorRowCount)
        {
            rows.Add(CreateEmptyRow(rows.Count, fallbackLaunchTarget));
        }
    }

    public static void EnsureMinimumRowsForEditor(List<LaunchRowDraft> rows, string fallbackLaunchTarget)
    {
        if (rows.Count >= MinimumEditorRowCount)
        {
            return;
        }

        EnsureMinimumRows(rows, fallbackLaunchTarget);
    }

    public static LaunchRowDraft CreateEmptyRow(int index, string fallbackLaunchTarget) =>
        new()
        {
            LaunchTarget = index == 0
                ? fallbackLaunchTarget
                : TerminalCatalog.SameAsPreviousLaunchTargetId,
            IsEditorPlaceholder = true,
        };

    public static void ClearRow(List<LaunchRowDraft> rows, int index)
    {
        if (index < 0 || index >= rows.Count)
        {
            return;
        }

        rows[index].Command = string.Empty;
        rows[index].TaskType = TaskTypeCatalog.None;
        rows[index].IsEditorPlaceholder = false;
    }

    public static bool ApplyPill(List<LaunchRowDraft> rows, CommandSuggestionPill pill, string fallbackLaunchTarget)
    {
        var targetIndex = FindFirstEmptyCommandIndex(rows);
        if (targetIndex < 0)
        {
            rows.Add(CreateEmptyRow(rows.Count, fallbackLaunchTarget));
            targetIndex = rows.Count - 1;
        }

        rows[targetIndex].Command = pill.Command;
        rows[targetIndex].TaskType = pill.TaskType;
        rows[targetIndex].IsEditorPlaceholder = false;
        return targetIndex == rows.Count - 1 && rows.Count > MinimumEditorRowCount;
    }

    public static int FindFirstEmptyCommandIndex(IReadOnlyList<LaunchRowDraft> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i].Command))
            {
                return i;
            }
        }

        return -1;
    }

    public static List<LaunchRowDraft> TrimForSave(IReadOnlyList<LaunchRowDraft> commands)
    {
        var rows = commands.Select(row => row.Clone()).ToList();
        rows.RemoveAll(row =>
            row.IsEditorPlaceholder
            && string.IsNullOrWhiteSpace(row.Command)
            && string.Equals(TaskTypeCatalog.Normalize(row.TaskType), TaskTypeCatalog.None, StringComparison.Ordinal));

        if (rows.Count == 0)
        {
            rows.Add(new LaunchRowDraft());
        }

        return rows;
    }

    public static List<LaunchRowDraft> CloneRows(IEnumerable<LaunchRowDraft> rows) =>
        rows.Select(row => row.Clone()).ToList();
}
