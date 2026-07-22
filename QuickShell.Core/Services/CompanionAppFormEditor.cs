using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Multi-companion form list helpers (add / remove / load from workspace).
/// Cap matches <see cref="CompanionAppNormalization.MaxCompanionCount"/>.
/// </summary>
internal static class CompanionAppFormEditor
{
    public const int MaxCount = CompanionAppNormalization.MaxCompanionCount;

    public const string AddAction = "addCompanionApp";
    public const string RemoveAction = "removeCompanionApp";
    public const string BrowseAction = "browseCompanionApp";

    public const string AddTooltip = "Add another companion app";
    public const string RemoveTooltip = "Remove this companion app";

    /// <summary>
    /// Loads companion rows for the workspace form. Avoids full install/path reconciliation
    /// on open (that path probes disk/PATH and made Create/Edit feel laggy); Save still
    /// reconciles via <see cref="ToCompanionEntries"/>.
    /// </summary>
    public static List<CompanionAppFormRow> FromShortcut(TerminalShortcut? shortcut)
    {
        if (shortcut is null)
        {
            return [CompanionAppFormRow.Empty()];
        }

        CompanionAppNormalization.EnsureCompanionsFromLegacy(shortcut);
        if (shortcut.CompanionApps.Count == 0)
        {
            return [CompanionAppFormRow.Empty()];
        }

        return shortcut.CompanionApps
            .OrderBy(entry => entry.Order)
            .Select(entry =>
            {
                var path = entry.Path?.Trim() ?? string.Empty;
                var preset = string.IsNullOrWhiteSpace(path)
                    ? CompanionAppCatalog.PresetNone
                    : CompanionAppCatalog.InferPresetFromFileName(path);
                return new CompanionAppFormRow
                {
                    Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
                    Preset = preset,
                    Path = path,
                    Arguments = entry.Arguments?.Trim() ?? string.Empty,
                    OpenOnLaunch = entry.OpenOnLaunch,
                };
            })
            .ToList();
    }

    public static void EnsureAtLeastOne(List<CompanionAppFormRow> rows)
    {
        if (rows.Count == 0)
        {
            rows.Add(CompanionAppFormRow.Empty());
        }
    }

    public static bool CanAdd(IReadOnlyList<CompanionAppFormRow> rows) =>
        rows.Count < MaxCount;

    public static bool TryAdd(List<CompanionAppFormRow> rows)
    {
        if (!CanAdd(rows))
        {
            return false;
        }

        rows.Add(CompanionAppFormRow.Empty());
        return true;
    }

    public static bool TryRemove(List<CompanionAppFormRow> rows, int index)
    {
        if (rows.Count <= 1 || index < 0 || index >= rows.Count)
        {
            return false;
        }

        rows.RemoveAt(index);
        EnsureAtLeastOne(rows);
        return true;
    }

    public static List<CompanionAppEntry> ToCompanionEntries(IReadOnlyList<CompanionAppFormRow> rows)
    {
        var entries = new List<CompanionAppEntry>();
        var order = 0;
        foreach (var row in rows)
        {
            var state = CompanionAppCatalog.ReconcileForSave(
                row.Preset,
                row.Path,
                row.Arguments,
                // A configured companion always opens with the workspace; forms no longer expose a toggle.
                openOnLaunch: true);
            if (string.IsNullOrWhiteSpace(state.Path))
            {
                continue;
            }

            entries.Add(new CompanionAppEntry
            {
                Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id,
                Path = state.Path,
                Arguments = string.IsNullOrWhiteSpace(state.Arguments) ? null : state.Arguments,
                OpenOnLaunch = state.LaunchOnWorkspaceOpen,
                Order = order++,
            });
        }

        return entries;
    }

    public static void SyncLegacyScalars(List<CompanionAppFormRow> rows, out bool openOnLaunch, out string path, out string arguments, out string preset)
    {
        EnsureAtLeastOne(rows);
        var primary = rows[0];
        openOnLaunch = primary.OpenOnLaunch;
        path = primary.Path;
        arguments = primary.Arguments;
        preset = primary.Preset;
    }
}
