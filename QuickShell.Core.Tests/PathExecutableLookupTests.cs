using QuickShell.Services;

namespace QuickShell.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PathExecutableLookupIsolation
{
    public const string Name = "PathExecutableLookup";
}

[Collection(PathExecutableLookupIsolation.Name)]
public sealed class PathExecutableLookupTests : IDisposable
{
    private readonly Func<string, string?>? _previous;

    public PathExecutableLookupTests()
    {
        _previous = PathExecutableLookup.TryResolveOverride;
        PathExecutableLookup.TryResolveOverride = null;
    }

    public void Dispose() => PathExecutableLookup.TryResolveOverride = _previous;

    [Fact]
    public void Exists_OverrideHit_ReturnsTrue()
    {
        PathExecutableLookup.TryResolveOverride = name =>
            name.Equals("wt.exe", StringComparison.OrdinalIgnoreCase)
                ? @"C:\fake\wt.exe"
                : null;

        Assert.True(PathExecutableLookup.Exists("wt.exe"));
        Assert.True(PathExecutableLookup.TryResolve("wt.exe", out var fullPath));
        Assert.Equal(@"C:\fake\wt.exe", fullPath);
    }

    [Fact]
    public void Exists_OverrideMiss_ReturnsFalse()
    {
        PathExecutableLookup.TryResolveOverride = _ => null;

        Assert.False(PathExecutableLookup.Exists("wt.exe"));
        Assert.False(PathExecutableLookup.Exists("cmd.exe"));
        Assert.False(PathExecutableLookup.TryResolve("pwsh.exe", out _));
    }

    [Fact]
    public void TryResolveKnownLocation_System32_FindsCmdAndPowerShell()
    {
        Assert.True(PathExecutableLookup.TryResolveKnownLocation("cmd.exe", out var cmdPath));
        Assert.True(File.Exists(cmdPath));
        Assert.Equal("cmd.exe", Path.GetFileName(cmdPath), ignoreCase: true);

        Assert.True(PathExecutableLookup.TryResolveKnownLocation("powershell.exe", out var psPath));
        Assert.True(File.Exists(psPath));
        Assert.Equal("powershell.exe", Path.GetFileName(psPath), ignoreCase: true);
        Assert.Contains("WindowsPowerShell", psPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveKnownLocation_Pwsh_IsNotKnown()
    {
        Assert.False(PathExecutableLookup.TryResolveKnownLocation("pwsh.exe", out _));
    }

    [Fact]
    public void TryResolve_WithoutOverride_UsesKnownLocationForCmd()
    {
        PathExecutableLookup.TryResolveOverride = null;

        Assert.True(PathExecutableLookup.TryResolve("cmd.exe", out var fullPath));
        Assert.True(File.Exists(fullPath));
        Assert.StartsWith(
            Environment.SystemDirectory,
            fullPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
