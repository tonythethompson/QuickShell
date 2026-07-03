using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class JetBrainsInstallDiscoveryTests : IDisposable
{
    private readonly string _channelRoot;
    private readonly string _executable;

    public JetBrainsInstallDiscoveryTests()
    {
        var channelName = "ch-test-" + Guid.NewGuid().ToString("N");
        _channelRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JetBrains",
            "Toolbox",
            "apps",
            "Rider",
            channelName);
        _executable = Path.Combine(_channelRoot, "241.12345.67", "bin", "rider64.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_executable)!);
        File.WriteAllText(_executable, string.Empty);
        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow);
    }

    [Fact]
    public void TryResolveRider_FindsToolboxBuildDirectoryExecutable()
    {
        var resolved = JetBrainsInstallDiscovery.TryResolveRider();

        Assert.NotNull(resolved);
        Assert.Equal(_executable, resolved, ignoreCase: true);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_channelRoot, recursive: true);
        }
        catch
        {
        }
    }
}
