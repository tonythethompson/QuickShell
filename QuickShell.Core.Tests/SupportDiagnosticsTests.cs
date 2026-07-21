using QuickShell.Services;
using System.Diagnostics;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class SupportDiagnosticsTests : IDisposable
{
    private readonly string _root;

    public SupportDiagnosticsTests()
    {
        _root = Path.Join(Path.GetTempPath(), "quickshell-support-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private SupportDiagnostics CreateDiagnostics(int? maximumLogFileBytes = null, string? logDirectory = null) =>
        new(new SupportDiagnosticsOptions(logDirectory ?? _root, maximumLogFileBytes));

    [Fact]
    public void Write_RedactsExceptionMessagesAndWritesStructuredEvent()
    {
        const string secret = @"C:\private\secret-project\launch.cmd";
        var diagnostics = CreateDiagnostics();

        diagnostics.WriteError("workspace_launch_failed", new InvalidOperationException(secret));

        var logPath = Assert.Single(Directory.GetFiles(_root, "*.jsonl"));
        var line = Assert.Single(File.ReadAllLines(logPath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("workspace_launch_failed", document.RootElement.GetProperty("eventCode").GetString());
        Assert.Equal("Error", document.RootElement.GetProperty("severity").GetString());
        Assert.Equal("InvalidOperationException", document.RootElement.GetProperty("exceptionType").GetString());
        Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
        Assert.DoesNotContain("message", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_RedactsMessageAndDataAsBoundedTags()
    {
        const string secret = @"C:\private\secret-project\npm.cmd";
        var diagnostics = CreateDiagnostics();

        diagnostics.Write("Program.cs:Main", secret, new { command = secret });

        var logPath = Assert.Single(Directory.GetFiles(_root, "*.jsonl"));
        var line = Assert.Single(File.ReadAllLines(logPath));
        using var document = JsonDocument.Parse(line);
        var tags = document.RootElement.GetProperty("tags").EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        Assert.Contains("data:present", tags);
        Assert.Contains(tags, tag => tag is not null && tag.StartsWith("message:sha256:", StringComparison.Ordinal));
        Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RotatesAndRetainsOnlyThreeLogs()
    {
        var diagnostics = CreateDiagnostics(maximumLogFileBytes: 1);

        for (var index = 0; index < 5; index++)
        {
            diagnostics.WriteInfo($"event.{index}");
        }

        var contents = string.Join("\n", Directory.GetFiles(_root, "*.jsonl").Select(File.ReadAllText));
        Assert.Equal(3, Directory.GetFiles(_root, "*.jsonl").Length);
        Assert.DoesNotContain("event.0", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("event.1", contents, StringComparison.Ordinal);
        Assert.Contains("event.4", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBundle_ReportsOnlyDiagnosticAggregates()
    {
        const string workspaceName = "Secret workspace C:\\private";
        const string command = "npm run private-command";
        var diagnostics = new LaunchDiagnosticsReport(workspaceName, DateTimeOffset.UtcNow);
        diagnostics.AddError(LaunchDiagnosticKind.HealthError, "Could not launch", command);

        var bundle = CreateDiagnostics().BuildBundle(diagnostics);

        Assert.Contains("schemaVersion", bundle, StringComparison.Ordinal);
        Assert.Contains("HealthError", bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("logDirectory", bundle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_root, bundle, StringComparison.Ordinal);
        Assert.DoesNotContain(workspaceName, bundle, StringComparison.Ordinal);
        Assert.DoesNotContain(command, bundle, StringComparison.Ordinal);
        Assert.DoesNotContain("Could not launch", bundle, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_DoesNotThrowWhenLogDirectoryCannotBeCreated()
    {
        var blockedPath = Path.Join(_root, "not-a-directory");
        File.WriteAllText(blockedPath, "file");
        var diagnostics = CreateDiagnostics(logDirectory: blockedPath);

        var exception = Record.Exception(() => diagnostics.WriteInfo("startup.complete"));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Cleanup failed for '{_root}' due to IO error: {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Cleanup failed for '{_root}' due to access error: {exception}");
        }
    }
}
