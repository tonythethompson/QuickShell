using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

[Collection("ShortcutRepositoryMutex")]
public sealed class WorkspaceSecurityAdversarialTests
{
    [Theory]
    [InlineData("echo one\necho two")]
    [InlineData("echo one\recho two")]
    [InlineData("echo one\0echo two")]
    public void InvalidCommand_rejects_control_characters(string command)
    {
        var content = WorkspaceTestingHelpers.CreateWorkspace(id: "workspace-adversarial");
        content.Command = command;
        content.Launches =
        [
            new WorkspaceEntry
            {
                Id = "launch-1",
                Label = "Launch",
                Command = command,
                IsEnabled = true,
            },
        ];
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata { IsTrusted = true }, 1);

        var launch = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.LaunchTerminal);
        var trust = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.GrantTrust);

        Assert.False(launch.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidCommand, launch.PrimaryIssueCode);
        Assert.False(trust.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidCommand, trust.PrimaryIssueCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/windows/system32/notepad.exe")]
    [InlineData("vbscript:msgbox(1)")]
    public void InvalidUrl_rejects_unsafe_schemes(string url)
    {
        var content = WorkspaceTestingHelpers.CreateWorkspace(id: "workspace-adversarial");
        content.DevServerUrl = url;
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata { IsTrusted = true }, 1);

        var result = WorkspaceSecurityPolicy.AuthorizeUrl(workspace, url, WorkspaceAction.OpenDevServer);

        Assert.False(result.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidUrl, result.PrimaryIssueCode);
    }

    [Theory]
    [InlineData(@"\\?\pipe\quickshell")]
    [InlineData(@"\\localhost\share\project")]
    [InlineData(@"%TEMP%\project")]
    public void OpenDirectory_rejects_unc_pipe_and_env_paths(string directory)
    {
        var content = WorkspaceTestingHelpers.CreateWorkspace(id: "workspace-adversarial");
        content.Directory = directory;
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata { IsTrusted = true }, 1);

        var open = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDirectory);

        Assert.False(open.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidDirectory, open.PrimaryIssueCode);
    }

    [Fact]
    public void Companion_rejects_newline_injection_in_path_and_arguments()
    {
        var content = WorkspaceTestingHelpers.CreateWorkspace(id: "workspace-adversarial");
        content.CompanionApps =
        [
            new CompanionAppEntry
            {
                Id = "companion-1",
                Path = Environment.ProcessPath + "\nbad",
                Arguments = "--folder\n--evil",
                OpenOnLaunch = true,
            },
        ];
        var workspace = new StoredWorkspace(content, new WorkspaceSecurityMetadata { IsTrusted = true }, 1);

        var companion = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.StartCompanion);
        var trust = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.GrantTrust);

        Assert.False(companion.IsAllowed);
        Assert.Equal(WorkspaceIssueCode.InvalidCompanion, companion.PrimaryIssueCode);
        Assert.False(trust.IsAllowed);
    }

    [Fact]
    public void Import_rejects_oversized_payload()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var importPath = Path.Join(directory.Path, "huge.json");
        // ~3 MB of padding exceeds the ~2 MB import size limit.
        var padding = new string('x', 3 * 1024 * 1024);
        File.WriteAllText(importPath, $$"""[{ "Name": "Huge", "Directory": "C:\\Temp", "Command": "{{padding}}" }]""");

        var result = repository.ImportMerge(importPath);

        Assert.False(result.Success);
        Assert.Contains("No valid shortcuts were found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_rejects_malformed_json()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var importPath = Path.Join(directory.Path, "broken.json");
        File.WriteAllText(importPath, "{ not-json");

        var result = repository.ImportMerge(importPath);

        Assert.False(result.Success);
        Assert.Contains("No valid shortcuts were found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_envelope_with_unsupported_version_fails_closed()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var importPath = Path.Join(directory.Path, "future.json");
        File.WriteAllText(importPath, """{"version":999,"entries":[]}""");

        var result = repository.ImportMerge(importPath);

        Assert.False(result.Success);
        Assert.Contains("No valid shortcuts were found", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
