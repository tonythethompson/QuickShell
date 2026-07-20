using System.Text.Json;
using QuickShell;
using QuickShell.Abstractions;

namespace QuickShell.Services;

internal sealed class WorktreeBranchTargetStore : IWorktreeBranchTargetStore
{
    private readonly IAppDataPaths _appDataPaths;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public WorktreeBranchTargetStore(IAppDataPaths appDataPaths, IAtomicFileWriter fileWriter)
    {
        _appDataPaths = appDataPaths ?? throw new ArgumentNullException(nameof(appDataPaths));
        _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
    }

    public string? GetTarget(string worktreeKey)
    {
        EnsureLoaded();
        lock (_sync)
        {
            return _targets.TryGetValue(worktreeKey, out var target) ? target : null;
        }
    }

    public string? GetTargetForDirectory(string directory, IWorkspaceGitOperations git)
    {
        ArgumentNullException.ThrowIfNull(git);

        if (!git.TryResolveWorktreeKey(directory, out var worktreeKey))
        {
            return null;
        }

        return GetTarget(worktreeKey);
    }

    public void SetTarget(string worktreeKey, string? branch)
    {
        EnsureLoaded();
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                _targets.Remove(worktreeKey);
            }
            else
            {
                _targets[worktreeKey] = branch.Trim();
            }

            SaveLocked();
        }
    }

    public bool TrySetTargetForDirectory(
        string directory,
        string? branch,
        IWorkspaceGitOperations git,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(git);
        error = null;

        if (!git.TryResolveWorktreeKey(directory, out var worktreeKey))
        {
            error = "This folder is not a git repository.";
            return false;
        }

        SetTarget(worktreeKey, branch);
        return true;
    }

    public void ClearTargetForDirectory(string directory, IWorkspaceGitOperations git)
    {
        ArgumentNullException.ThrowIfNull(git);

        if (!git.TryResolveWorktreeKey(directory, out var worktreeKey))
        {
            return;
        }

        SetTarget(worktreeKey, null);
    }

    private void EnsureLoaded()
    {
        lock (_sync)
        {
            if (_loaded)
            {
                return;
            }

            if (LoadLocked())
            {
                _loaded = true;
            }
        }
    }

    private bool LoadLocked()
    {
        _targets.Clear();

        var path = ResolveFilePath();
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize(
                stream,
                QuickShellJsonContext.Default.WorktreeBranchTargetsDocument);
            if (document?.Targets is not { Count: > 0 } persistedTargets)
            {
                return true;
            }

            foreach (var (worktreeKey, branch) in persistedTargets)
            {
                if (string.IsNullOrWhiteSpace(worktreeKey) || string.IsNullOrWhiteSpace(branch))
                {
                    continue;
                }

                if (WorkspaceGitOperations.TryNormalizeWorktreeKey(worktreeKey, out var normalizedKey))
                {
                    _targets[normalizedKey] = branch.Trim();
                }
                else
                {
                    _targets[worktreeKey] = branch.Trim();
                }
            }

            return true;
        }
        catch (IOException)
        {
            _targets.Clear();
            return false;
        }
        catch (Exception ex) when (ex is JsonException or UnauthorizedAccessException)
        {
            _targets.Clear();
            return true;
        }
    }

    private void SaveLocked()
    {
        var path = ResolveFilePath();
        var payload = new WorktreeBranchTargetsDocument
        {
            Targets = _targets.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
        };

        _fileWriter.WriteAllTextAtomic(
            path,
            JsonSerializer.Serialize(payload, QuickShellJsonContext.Default.WorktreeBranchTargetsDocument));
    }

    private string ResolveFilePath() =>
        Path.Combine(_appDataPaths.Root, "QuickShell", "worktree-branch-targets.json");
}
