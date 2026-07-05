using System.Diagnostics;
using System.Text;

namespace QuickShell.Services;

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    public static GitCommandResult Failed { get; } = new(-1, string.Empty, string.Empty, TimedOut: false);
}

internal sealed record WorkspaceGitStatus(string Branch, bool IsDirty, bool IsDetached);

internal static class WorkspaceGitOperations
{
    private const int GitTimeoutMs = 3000;

    internal static Func<string, IReadOnlyList<string>, GitCommandResult>? GitRunOverride { get; set; }

    internal static Func<string, WorkspaceGitStatus?>? GitStatusOverride { get; set; }

    public static bool TryResolveWorktreeKey(string directory, out string worktreeKey)
    {
        worktreeKey = string.Empty;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var insideWorkTree = RunGit(directory, ["rev-parse", "--is-inside-work-tree"]);
        if (!insideWorkTree.Succeeded
            || !string.Equals(insideWorkTree.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var topLevel = RunGit(directory, ["rev-parse", "--show-toplevel"]);
        if (!topLevel.Succeeded || string.IsNullOrWhiteSpace(topLevel.StandardOutput))
        {
            return false;
        }

        return TryNormalizeWorktreeKey(topLevel.StandardOutput.Trim(), out worktreeKey);
    }

    public static bool TryGetStatus(string directory, out WorkspaceGitStatus status)
    {
        status = null!;

        if (GitStatusOverride is { } statusOverride)
        {
            var overridden = statusOverride(directory);
            if (overridden is null)
            {
                return false;
            }

            status = overridden;
            return true;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var insideWorkTree = RunGit(directory, ["rev-parse", "--is-inside-work-tree"]);
        if (!insideWorkTree.Succeeded
            || !string.Equals(insideWorkTree.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var branchResult = RunGit(directory, ["rev-parse", "--abbrev-ref", "HEAD"]);
        if (!branchResult.Succeeded || string.IsNullOrWhiteSpace(branchResult.StandardOutput))
        {
            return false;
        }

        var branchName = branchResult.StandardOutput.Trim();
        var isDetached = branchName.Equals("HEAD", StringComparison.Ordinal);
        var porcelain = RunGit(directory, ["status", "--porcelain"]);
        status = new WorkspaceGitStatus(
            isDetached ? "(detached)" : branchName,
            !string.IsNullOrWhiteSpace(porcelain.StandardOutput),
            isDetached);
        return true;
    }

    public static IReadOnlyList<string> ListLocalBranches(string directory)
    {
        var result = RunGit(directory, ["for-each-ref", "refs/heads", "--format=%(refname:short)"]);
        if (!result.Succeeded)
        {
            return [];
        }

        return result.StandardOutput
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsOnBranch(WorkspaceGitStatus status, string targetBranch) =>
        !status.IsDetached
        && string.Equals(status.Branch, targetBranch, StringComparison.Ordinal);

    public static bool TrySwitchBranch(string directory, string branch, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(branch))
        {
            error = "Branch name is required.";
            return false;
        }

        var result = RunGit(directory, ["switch", branch]);
        if (result.Succeeded)
        {
            return true;
        }

        error = FormatSwitchError(branch, result);
        return false;
    }

    public static string FormatBranchContextLabel(
        WorkspaceGitStatus? status,
        string? targetBranch)
    {
        if (status is null)
        {
            return "Branch";
        }

        var dirtySuffix = status.IsDirty ? " · dirty" : string.Empty;
        var current = status.Branch;

        if (string.IsNullOrWhiteSpace(targetBranch))
        {
            return $"Branch: {current}{dirtySuffix}";
        }

        if (IsOnBranch(status, targetBranch))
        {
            return $"Branch: {targetBranch}{dirtySuffix}";
        }

        return $"Branch: {current} → {targetBranch}{dirtySuffix}";
    }

    public static GitCommandResult RunGit(string directory, IReadOnlyList<string> gitArguments)
    {
        if (GitRunOverride is { } gitRunOverride)
        {
            return gitRunOverride(directory, gitArguments);
        }

        if (string.IsNullOrWhiteSpace(directory) || gitArguments.Count == 0)
        {
            return GitCommandResult.Failed;
        }

        try
        {
            if (WslPathResolver.TryParse(directory, out var wslLocation))
            {
                return RunGitViaWsl(wslLocation, gitArguments);
            }

            return RunGitProcess("git.exe", BuildNativeGitArguments(directory, gitArguments));
        }
        catch
        {
            return GitCommandResult.Failed;
        }
    }

    internal static bool TryNormalizeWorktreeKey(string topLevelPath, out string worktreeKey)
    {
        worktreeKey = string.Empty;

        if (string.IsNullOrWhiteSpace(topLevelPath))
        {
            return false;
        }

        if (WslPathResolver.TryParse(topLevelPath, out var wslLocation))
        {
            if (!string.IsNullOrWhiteSpace(wslLocation.UncPath))
            {
                worktreeKey = WorkspacePath.TrimTrailingSeparatorsForStorage(wslLocation.UncPath);
                return worktreeKey.Length > 0;
            }

            if (topLevelPath.StartsWith('/') && !topLevelPath.StartsWith("//", StringComparison.Ordinal))
            {
                worktreeKey = topLevelPath.TrimEnd('/');
                return worktreeKey.Length > 0;
            }
        }

        if (WorkspacePath.TryNormalizeLexical(topLevelPath, out var normalized, out _))
        {
            worktreeKey = normalized;
            return true;
        }

        try
        {
            worktreeKey = Path.GetFullPath(topLevelPath);
            worktreeKey = WorkspacePath.TrimTrailingSeparatorsForStorage(worktreeKey);
            return worktreeKey.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static GitCommandResult RunGitViaWsl(
        WslPathResolver.WslLocation wslLocation,
        IReadOnlyList<string> gitArguments)
    {
        var distro = wslLocation.Distro ?? "Ubuntu";
        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(distro);
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("git");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(wslLocation.LinuxPath);
        foreach (var argument in gitArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunGitProcess(startInfo);
    }

    private static IEnumerable<string> BuildNativeGitArguments(string directory, IReadOnlyList<string> gitArguments)
    {
        yield return "-C";
        yield return directory;
        foreach (var argument in gitArguments)
        {
            yield return argument;
        }
    }

    private static GitCommandResult RunGitProcess(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunGitProcess(startInfo);
    }

    private static GitCommandResult RunGitProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return GitCommandResult.Failed;
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(GitTimeoutMs))
        {
            TryKill(process);
            return new GitCommandResult(-1, outputBuilder.ToString(), errorBuilder.ToString(), TimedOut: true);
        }

        process.WaitForExit();
        return new GitCommandResult(
            process.ExitCode,
            outputBuilder.ToString().Trim(),
            errorBuilder.ToString().Trim(),
            TimedOut: false);
    }

    private static string FormatSwitchError(string branch, GitCommandResult result)
    {
        if (result.TimedOut)
        {
            return $"Could not switch to branch '{branch}': git timed out.";
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError;
        }

        return $"Could not switch to branch '{branch}'.";
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
