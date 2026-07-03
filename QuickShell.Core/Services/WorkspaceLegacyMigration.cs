using System.Text.Json;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceLegacyMigration
{
    public static bool TryReadLegacyWorkspaces(
        string configDirectory,
        IShortcutRepository shortcuts,
        out List<TerminalShortcut> imported,
        out string? error)
    {
        imported = [];
        error = null;

        var workspacesPath = Path.Combine(configDirectory, "workspaces.json");
        if (!File.Exists(workspacesPath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(workspacesPath);
            if (fileInfo.Length > 2 * 1024 * 1024)
            {
                error = "Legacy workspaces file is too large.";
                return false;
            }

            var bytes = File.ReadAllBytes(workspacesPath);
            var records = JsonSerializer.Deserialize(bytes, QuickShellJsonContext.Default.ListWorkspaceDiskRecord) ?? [];

            var seenWorkspaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                var result = WorkspaceValidation.NormalizeForLoad(record, shortcuts, seenWorkspaceIds);
                if (result.Workspace is null)
                {
                    continue;
                }

                imported.Add(ShortcutLaunchNormalization.WorkspaceToShortcut(result.Workspace));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            imported = [];
            return false;
        }
    }

    public static void ArchiveWorkspacesFile(string configDirectory)
    {
        var workspacesPath = Path.Combine(configDirectory, "workspaces.json");
        if (!File.Exists(workspacesPath))
        {
            return;
        }

        var archivePath = Path.Combine(configDirectory, "workspaces.json.migrated");
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            File.Move(workspacesPath, archivePath);
        }
        catch
        {
            // Best effort — in-memory state is already merged.
        }
    }

    public static string ResolveAvailableName(string desiredName, IEnumerable<TerminalShortcut> existing)
    {
        var names = existing.Select(shortcut => shortcut.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(desiredName))
        {
            return desiredName;
        }

        for (var i = 2; i <= 999; i++)
        {
            var candidate = $"{desiredName} ({i})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{desiredName} ({Guid.NewGuid():N})";
    }
}
