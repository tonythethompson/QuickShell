using QuickShell.Core.Services;
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
                Kind = string.IsNullOrWhiteSpace(entry.Command)
                    ? LaunchRowKind.OpenInTerminal
                    : LaunchRowKind.Command,
                Id = entry.Id,
                Label = entry.Label ?? string.Empty,
                Command = entry.Command ?? string.Empty,
                TaskType = TaskTypeCatalog.Normalize(entry.TaskType),
                LaunchTarget = ShortcutFormSave.EncodeLaunchTargetForEntry(entry),
                RunAsAdmin = entry.RunAsAdmin,
                IsEnabled = entry.IsEnabled,
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

    public static void EnsureMinimumRowsForEditor(List<LaunchRowDraft> rows, string fallbackLaunchTarget) =>
        EnsureMinimumRows(rows, fallbackLaunchTarget);

    public static LaunchRowDraft CreateEmptyRow(int index, string fallbackLaunchTarget) =>
        new()
        {
            Kind = LaunchRowKind.Command,
            LaunchTarget = index == 0
                ? fallbackLaunchTarget
                : TerminalCatalog.SameAsPreviousLaunchTargetId,
            IsEditorPlaceholder = true,
        };

    /// <summary>
    /// Removes the launch row at <paramref name="index"/> while preserving the effective
    /// launch target of a following "same as previous" row.
    /// </summary>
    public static void RemoveRow(List<LaunchRowDraft> rows, int index, string fallbackLaunchTarget)
    {
        if (index < 0 || index >= rows.Count)
        {
            return;
        }

        var successor = index + 1 < rows.Count ? rows[index + 1] : null;
        if (successor?.LaunchTarget.Equals(TerminalCatalog.SameAsPreviousLaunchTargetId, StringComparison.OrdinalIgnoreCase) == true)
        {
            successor.LaunchTarget = ResolveEffectiveLaunchTarget(rows, index, fallbackLaunchTarget);
        }

        rows.RemoveAt(index);
    }

    public static bool ApplyPill(List<LaunchRowDraft> rows, CommandSuggestionPill pill, string fallbackLaunchTarget)
    {
        var targetIndex = FindFirstEmptyCommandIndex(rows);
        if (targetIndex < 0)
        {
            rows.Add(CreateEmptyRow(rows.Count, fallbackLaunchTarget));
            targetIndex = rows.Count - 1;
        }

        rows[targetIndex].Kind = LaunchRowKind.Command;
        rows[targetIndex].Command = pill.Command;
        rows[targetIndex].TaskType = pill.TaskType;
        rows[targetIndex].IsEditorPlaceholder = false;
        if (string.IsNullOrWhiteSpace(rows[targetIndex].Label))
        {
            rows[targetIndex].Label = CreateUniqueLabel(rows, targetIndex, "Command");
        }

        return true;
    }

    /// <summary>
    /// First empty editor placeholder, so pills refill gaps after clear/compact without
    /// overwriting intentional folder-only launches.
    /// </summary>
    public static int FindFirstEmptyCommandIndex(IReadOnlyList<LaunchRowDraft> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == LaunchRowKind.Command
                && string.IsNullOrWhiteSpace(rows[i].Command))
            {
                return i;
            }
        }

        return -1;
    }

    public static List<LaunchRowDraft> TrimForSave(IReadOnlyList<LaunchRowDraft> commands)
    {
        var rows = commands.Select(row => row.Clone()).ToList();
        rows.RemoveAll(row => row.Kind == LaunchRowKind.Command && string.IsNullOrWhiteSpace(row.Command));

        foreach (var row in rows.Where(row => row.Kind == LaunchRowKind.OpenInTerminal))
        {
            row.Command = string.Empty;
            row.IsEditorPlaceholder = false;
        }

        return rows;
    }

    public static List<LaunchRowDraft> CloneRows(IEnumerable<LaunchRowDraft> rows) =>
        rows.Select(row => row.Clone()).ToList();

    private static string ResolveEffectiveLaunchTarget(
        List<LaunchRowDraft> rows,
        int index,
        string fallbackLaunchTarget)
    {
        for (var i = index; i >= 0; i--)
        {
            var target = rows[i].LaunchTarget;
            if (!string.IsNullOrWhiteSpace(target)
                && !target.Equals(TerminalCatalog.SameAsPreviousLaunchTargetId, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }
        }

        return fallbackLaunchTarget;
    }

    private static string CreateUniqueLabel(IReadOnlyList<LaunchRowDraft> rows, int targetIndex, string labelBase)
    {
        var labels = rows
            .Where((_, index) => index != targetIndex)
            .Select(row => row.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!labels.Contains(labelBase))
        {
            return labelBase;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{labelBase} {suffix}";
            if (!labels.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
