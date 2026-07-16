using QuickShell.Services;
using System.Text.Json;

namespace QuickShell.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SupportDiagnosticsIsolation
{
    public const string Name = nameof(SupportDiagnosticsIsolation);
}

[Collection(SupportDiagnosticsIsolation.Name)]
public sealed class SupportDiagnosticsTests : IDisposable
{
    private readonly string _root;

    public SupportDiagnosticsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-support-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SupportDiagnostics.LogDirectoryOverride = _root;
        SupportDiagnostics.MaximumLogFileBytesOverride = null;
    }

    [Fact]
    public void Write_RedactsExceptionMessagesAndWritesStructuredEvent()
    {
        const string secret = @"C:\private\secret-project\launch.cmd";

        SupportDiagnostics.WriteError("workspace.launch.failed", new InvalidOperationException(secret));

        var logPath = Assert.Single(Directory.GetFiles(_root, "*.jsonl"));
        var line = Assert.Single(File.ReadAllLines(logPath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("workspace.launch.failed", document.RootElement.GetProperty("eventCode").GetString());
        Assert.Equal("Error", document.RootElement.GetProperty("severity").GetString());
        Assert.Equal("InvalidOperationException", document.RootElement.GetProperty("exceptionType").GetString());
        Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
        Assert.DoesNotContain("message", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_RedactsCompatibilityParametersAsBoundedTags()
    {
        const string secret = @"C:\private\secret-project\npm.cmd";

        SupportDiagnostics.Write(
            "Program.cs:Main",
            secret,
            new { command = secret },
            hypothesisId: "A",
            runId: secret);

        var logPath = Assert.Single(Directory.GetFiles(_root, "*.jsonl"));
        var line = Assert.Single(File.ReadAllLines(logPath));
        using var document = JsonDocument.Parse(line);
        var tags = document.RootElement.GetProperty("tags").EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        Assert.Contains("data:present", tags);
        Assert.Contains("hypothesis:a", tags);
        Assert.Contains(tags, tag => tag is not null && tag.StartsWith("message:sha256:", StringComparison.Ordinal));
        Assert.Contains(tags, tag => tag is not null && tag.StartsWith("run:sha256:", StringComparison.Ordinal));
        Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RotatesAndRetainsOnlyThreeLogs()
    {
        SupportDiagnostics.MaximumLogFileBytesOverride = 1;

        for (var index = 0; index < 5; index++)
        {
            SupportDiagnostics.WriteInfo($"event.{index}");
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

        var bundle = SupportDiagnostics.BuildBundle(diagnostics);

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
        var blockedPath = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blockedPath, "file");
        SupportDiagnostics.LogDirectoryOverride = blockedPath;

        var exception = Record.Exception(() => SupportDiagnostics.WriteInfo("startup.complete"));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        SupportDiagnostics.ResetForTests();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
