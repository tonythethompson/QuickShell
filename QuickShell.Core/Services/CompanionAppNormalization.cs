using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Dual-read / dual-write between legacy scalar companion fields and the
/// ordered <see cref="TerminalShortcut.CompanionApps"/> list (same pattern as launch rows).
/// </summary>
internal static class CompanionAppNormalization
{
    public const int MaxCompanionCount = 5;

    public static void EnsureCompanionsFromLegacy(TerminalShortcut shortcut)
    {
        shortcut.CompanionApps ??= [];

        if (shortcut.CompanionApps.Count > 0)
        {
            DropEmptyCompanions(shortcut);
            if (shortcut.CompanionApps.Count > 0)
            {
                NormalizeOrders(shortcut);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(shortcut.CompanionAppPath))
        {
            shortcut.CompanionApps = [];
            return;
        }

        shortcut.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = shortcut.CompanionAppPath.Trim(),
                Arguments = string.IsNullOrWhiteSpace(shortcut.CompanionAppArguments)
                    ? null
                    : shortcut.CompanionAppArguments,
                OpenOnLaunch = shortcut.OpenCompanionAppOnLaunch,
                Order = 0,
            },
        ];
    }

    /// <summary>
    /// Keeps the first configured companion in sync with the legacy scalar fields so older
    /// hosts and form UIs that only know the triple still round-trip correctly.
    /// </summary>
    public static void MirrorLegacyFieldsFromPrimary(TerminalShortcut shortcut)
    {
        EnsureCompanionsFromLegacy(shortcut);
        var primary = GetPrimary(shortcut);
        if (primary is null)
        {
            shortcut.OpenCompanionAppOnLaunch = false;
            shortcut.CompanionAppPath = null;
            shortcut.CompanionAppArguments = null;
            return;
        }

        shortcut.CompanionAppPath = string.IsNullOrWhiteSpace(primary.Path) ? null : primary.Path.Trim();
        shortcut.CompanionAppArguments = string.IsNullOrWhiteSpace(primary.Arguments)
            ? null
            : primary.Arguments;
        shortcut.OpenCompanionAppOnLaunch = primary.OpenOnLaunch;
    }

    public static void NormalizeCompanions(TerminalShortcut shortcut)
    {
        EnsureCompanionsFromLegacy(shortcut);

        foreach (var entry in shortcut.CompanionApps)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString("N");
            }

            entry.Path = string.IsNullOrWhiteSpace(entry.Path) ? null : entry.Path.Trim();
            entry.Arguments = string.IsNullOrWhiteSpace(entry.Arguments) ? null : entry.Arguments;
        }

        DropEmptyCompanions(shortcut);
        if (shortcut.CompanionApps.Count > MaxCompanionCount)
        {
            shortcut.CompanionApps = shortcut.CompanionApps
                .OrderBy(entry => entry.Order)
                .Take(MaxCompanionCount)
                .ToList();
        }

        NormalizeOrders(shortcut);
        MirrorLegacyFieldsFromPrimary(shortcut);
    }

    /// <summary>
    /// Applies form / seed scalar companion fields as the primary entry while preserving
    /// additional companions already stored on the workspace.
    /// </summary>
    public static void ApplyPrimaryFromScalars(
        TerminalShortcut shortcut,
        bool openOnLaunch,
        string? path,
        string? arguments,
        IReadOnlyList<CompanionAppEntry>? preserveAdditionalFrom = null)
    {
        var additional = (preserveAdditionalFrom ?? shortcut.CompanionApps)
            .OrderBy(entry => entry.Order)
            .Skip(1)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .Select(CloneEntry)
            .ToList();

        var trimmedPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        var list = new List<CompanionAppEntry>();
        if (trimmedPath is not null)
        {
            var primaryId = (preserveAdditionalFrom ?? shortcut.CompanionApps)
                .OrderBy(entry => entry.Order)
                .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Path))
                ?.Id;
            list.Add(new CompanionAppEntry
            {
                Id = string.IsNullOrWhiteSpace(primaryId) ? Guid.NewGuid().ToString("N") : primaryId,
                Path = trimmedPath,
                Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments,
                OpenOnLaunch = openOnLaunch,
                Order = 0,
            });
        }

        list.AddRange(additional);
        shortcut.CompanionApps = list;
        NormalizeCompanions(shortcut);
    }

    public static IReadOnlyList<CompanionAppEntry> GetConfigured(TerminalShortcut shortcut)
    {
        EnsureCompanionsFromLegacy(shortcut);
        return shortcut.CompanionApps
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .OrderBy(entry => entry.Order)
            .ToList();
    }

    public static IReadOnlyList<CompanionAppEntry> GetOpenOnLaunch(TerminalShortcut shortcut) =>
        GetConfigured(shortcut)
            .Where(entry => entry.OpenOnLaunch)
            .ToList();

    public static CompanionAppEntry? GetPrimary(TerminalShortcut shortcut)
    {
        var configured = GetConfigured(shortcut);
        return configured.Count > 0 ? configured[0] : null;
    }

    public static bool TryValidateCompanions(TerminalShortcut shortcut, out string error)
    {
        EnsureCompanionsFromLegacy(shortcut);
        if (shortcut.CompanionApps.Count > MaxCompanionCount)
        {
            error = $"At most {MaxCompanionCount} companion apps are supported.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static CompanionAppEntry CloneEntry(CompanionAppEntry entry) => new()
    {
        Id = entry.Id,
        Path = entry.Path,
        Arguments = entry.Arguments,
        OpenOnLaunch = entry.OpenOnLaunch,
        Order = entry.Order,
    };

    private static void DropEmptyCompanions(TerminalShortcut shortcut) =>
        shortcut.CompanionApps = shortcut.CompanionApps
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .ToList();

    private static void NormalizeOrders(TerminalShortcut shortcut)
    {
        var ordered = shortcut.CompanionApps.OrderBy(entry => entry.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        shortcut.CompanionApps = ordered;
    }
}
