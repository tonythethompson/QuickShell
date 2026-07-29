using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class TerminalFragmentDiscoveryTests : IDisposable
{
    private readonly string _root;

    public TerminalFragmentDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-fragments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void LoadAll_MissingRoot_ReturnsEmpty()
    {
        var profiles = TerminalFragmentDiscovery.LoadAll([Path.Combine(_root, "missing")]);

        Assert.Empty(profiles);
    }

    [Fact]
    public void LoadAll_ReadsIconAndCommandlineFromFragment()
    {
        WriteFragment(
            Path.Combine(_root, "nu", "nu.json"),
            """
            {
                "profiles": [
                    {
                        "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "name": "Nushell",
                        "commandline": "C:/Users/tony/nu/bin/nu.exe",
                        "icon": "C:/Users/tony/nu/nu.ico"
                    }
                ]
            }
            """);

        var profiles = TerminalFragmentDiscovery.LoadAll([_root]);

        Assert.True(profiles.TryGetValue("47302f9c-1ac4-566c-aa3e-8cf29889d6ab", out var profile));
        Assert.Equal("C:/Users/tony/nu/bin/nu.exe", profile.Commandline);
        Assert.Equal("C:/Users/tony/nu/nu.ico", profile.Icon);
    }

    [Fact]
    public void LoadAll_LaterFileOverridesEarlierFileForSameGuid()
    {
        WriteFragment(
            Path.Combine(_root, "a", "first.json"),
            """
            {
                "profiles": [
                    {
                        "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "icon": "first.ico"
                    }
                ]
            }
            """);

        WriteFragment(
            Path.Combine(_root, "b", "second.json"),
            """
            {
                "profiles": [
                    {
                        "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "icon": "second.ico"
                    }
                ]
            }
            """);

        var profiles = TerminalFragmentDiscovery.LoadAll([_root]);

        Assert.True(profiles.TryGetValue("47302f9c-1ac4-566c-aa3e-8cf29889d6ab", out var profile));
        Assert.Equal("second.ico", profile.Icon);
    }

    [Fact]
    public void LoadAll_InvalidJsonIsIgnored()
    {
        WriteFragment(
            Path.Combine(_root, "broken.json"),
            "not json");

        var profiles = TerminalFragmentDiscovery.LoadAll([_root]);

        Assert.Empty(profiles);
    }

    private static void WriteFragment(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
