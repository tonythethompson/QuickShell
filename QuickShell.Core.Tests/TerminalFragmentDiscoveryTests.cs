using QuickShell.Services;
using System.Threading;

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
        Assert.EndsWith("second.ico", profile.Icon, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAll_UpdatesKey_MergesOntoExistingGuid()
    {
        WriteFragment(
            Path.Combine(_root, "base", "base.json"),
            """
            {
                "profiles": [
                    {
                        "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "icon": "base.ico"
                    }
                ]
            }
            """);

        WriteFragment(
            Path.Combine(_root, "patch", "patch.json"),
            """
            {
                "profiles": [
                    {
                        "updates": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "commandline": "C:/tools/nu.exe"
                    }
                ]
            }
            """);

        var profiles = TerminalFragmentDiscovery.LoadAll([_root]);

        Assert.True(profiles.TryGetValue("47302f9c-1ac4-566c-aa3e-8cf29889d6ab", out var profile));
        Assert.Equal("C:/tools/nu.exe", profile.Commandline);
        Assert.EndsWith("base.ico", profile.Icon, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAll_ResolvesRelativeIconAgainstFragmentDirectory()
    {
        var fragmentDir = Path.Combine(_root, "vendor");
        WriteFragment(
            Path.Combine(fragmentDir, "app.json"),
            """
            {
                "profiles": [
                    {
                        "guid": "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}",
                        "icon": "app_icon.png"
                    }
                ]
            }
            """);

        var profiles = TerminalFragmentDiscovery.LoadAll([_root]);

        Assert.True(profiles.TryGetValue("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", out var profile));
        Assert.Equal(Path.GetFullPath(Path.Combine(fragmentDir, "app_icon.png")), profile.Icon);
    }

    [Fact]
    public void ComputeFingerprint_ChangesWhenFileIsRewritten()
    {
        var path = Path.Combine(_root, "nu", "nu.json");
        WriteFragment(
            path,
            """
            {
                "profiles": [
                    {
                        "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "icon": "a.ico"
                    }
                ]
            }
            """);

        var first = TerminalFragmentDiscovery.ComputeFingerprint([_root]);
        Thread.Sleep(20);
        WriteFragment(
            path,
            """
            {
                "profiles": [
                    {
                        "guid": "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
                        "icon": "b.ico"
                    }
                ]
            }
            """);

        var second = TerminalFragmentDiscovery.ComputeFingerprint([_root]);
        Assert.NotEqual(first, second);
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
