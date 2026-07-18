using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceGitOperationsTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ParsePorcelainV2Status_CleanRepository_HandlesLineEndings(string lineEnding)
    {
        var output = $"# branch.oid abc123{lineEnding}# branch.head main{lineEnding}";

        var status = WorkspaceGitOperations.ParsePorcelainV2Status(output);

        Assert.NotNull(status);
        Assert.Equal("main", status.Branch);
        Assert.False(status.IsDirty);
        Assert.False(status.IsDetached);
    }

    [Fact]
    public void ParsePorcelainV2Status_DirtyRepository_DetectsTrackedEntry()
    {
        const string output = """
            # branch.oid abc123
            # branch.head feature/work
            1 M. N... 100644 100644 100644 abc123 def456 app.cs
            """;

        var status = WorkspaceGitOperations.ParsePorcelainV2Status(output);

        Assert.NotNull(status);
        Assert.True(status.IsDirty);
    }

    [Fact]
    public void ParsePorcelainV2Status_MergeConflict_DetectsDirtyRepository()
    {
        const string output = """
            # branch.oid abc123
            # branch.head main
            u UU N... 100644 100644 100644 100644 abc123 def456 fedcba conflict.cs
            """;

        var status = WorkspaceGitOperations.ParsePorcelainV2Status(output);

        Assert.NotNull(status);
        Assert.True(status.IsDirty);
    }

    [Fact]
    public void ParsePorcelainV2Status_DetachedHead_ReturnsDetachedStatus()
    {
        const string output = """
            # branch.oid abc123
            # branch.head (detached)
            """;

        var status = WorkspaceGitOperations.ParsePorcelainV2Status(output);

        Assert.NotNull(status);
        Assert.Equal("(detached)", status.Branch);
        Assert.True(status.IsDetached);
    }

    [Fact]
    public void ParsePorcelainV2Status_UnbornRepository_UsesBranchName()
    {
        const string output = """
            # branch.oid (initial)
            # branch.head main
            """;

        var status = WorkspaceGitOperations.ParsePorcelainV2Status(output);

        Assert.NotNull(status);
        Assert.Equal("main", status.Branch);
        Assert.False(status.IsDirty);
        Assert.False(status.IsDetached);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not porcelain output")]
    [InlineData("# branch.oid abc123")]
    public void ParsePorcelainV2Status_MissingBranchHead_ReturnsNull(string output)
    {
        var status = WorkspaceGitOperations.ParsePorcelainV2Status(output);

        Assert.Null(status);
    }
}
