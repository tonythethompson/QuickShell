using System.Text.Json;
using QuickShell;

namespace QuickShell.Services;

internal static class WorktreeBranchTargetStore
{
    internal static string? FilePathOverride { get; set; }

    internal static Func<string, string?>? GetTargetOverride { get; set; }

    internal static Action<string, string?>? SetTargetOverride { get; set; }

    private static readonly object Sync = new();

    private static Dictionary<string, string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static bool _loaded;

    public static string? GetTarget(string worktreeKey)
    {
        if (GetTargetOverride is { } getOverride)
        {
            return getOverride(worktreeKey);
        }

        EnsureLoaded();
        lock (Sync)
        {
            return Targets.TryGetValue(worktreeKey, out var target) ? target : null;
        }
    }

    public static string? GetTargetForDirectory(string directory)
    {
        if (!WorkspaceGitOperations.TryResolveWorktreeKey(directory, out var worktreeKey))
        {
            return null;
        }

        return GetTarget(worktreeKey);
    }

    public static void SetTarget(string worktreeKey, string? branch)
    {
        if (SetTargetOverride is { } setOverride)
        {
            setOverride(worktreeKey, branch);
            return;
        }

        EnsureLoaded();
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                Targets.Remove(worktreeKey);
            }
            else
            {
                Targets[worktreeKey] = branch.Trim();
            }

            SaveLocked();
        }
    }

    public static bool TrySetTargetForDirectory(string directory, string? branch, out string? error)
    {
        error = null;

        if (!WorkspaceGitOperations.TryResolveWorktreeKey(directory, out var worktreeKey))
        {
            error = "This folder is not a git repository.";
            return false;
        }

        SetTarget(worktreeKey, branch);
        return true;
    }

    public static void ClearTargetForDirectory(string directory)
    {
        if (!WorkspaceGitOperations.TryResolveWorktreeKey(directory, out var worktreeKey))
        {
            return;
        }

        SetTarget(worktreeKey, null);
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            Targets.Clear();
            _loaded = false;
        }
    }

    private static void EnsureLoaded()
    {
        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            LoadLocked();
            _loaded = true;
        }
    }

    private static void LoadLocked()
    {
        Targets.Clear();

        var path = ResolveFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var jsonDoc = JsonDocument.Parse(stream);

            if (!jsonDoc.RootElement.TryGetProperty("Targets", out var targetsElement))
            {
                return;
            }

            if (targetsElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in targetsElement.EnumerateObject())
            {
                var worktreeKey = property.Name;
                if (string.IsNullOrWhiteSpace(worktreeKey))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var branch = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(branch))
                {
                    continue;
                }

                if (WorkspaceGitOperations.TryNormalizeWorktreeKey(worktreeKey, out var normalizedKey))
                {
                    Targets[normalizedKey] = branch.Trim();
                }
                else
                {
                    Targets[worktreeKey] = branch.Trim();
                }
            }
        }
        catch (JsonException)
        {
            Targets.Clear();
        }
        catch
        {
        }
    }

    private static void SaveLocked()
    {
        var path = ResolveFilePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new WorktreeBranchTargetsDocument
        {
            Targets = Targets.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, QuickShellJsonContext.Default.WorktreeBranchTargetsDocument));
    }

    private static string ResolveFilePath() =>
        FilePathOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "worktree-branch-targets.json");
}
