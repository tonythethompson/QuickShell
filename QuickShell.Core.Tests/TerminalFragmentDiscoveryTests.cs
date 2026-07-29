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
        Assert.EndsWith("second.ico", profile.Icon, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void load_all_updates_key_merges_onto_existing_guid()
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
    public void load_all_resolves_relative_icon_against_fragment_directory()
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
    public void resolve_fragment_icon_preserves_inline_glyph()
    {
        var fragmentDir = Path.Combine(_root, "vendor");
        Directory.CreateDirectory(fragmentDir);

        Assert.Equal("🐧", TerminalFragmentDiscovery.ResolveFragmentIcon("🐧", fragmentDir));
        Assert.Equal("\uE756", TerminalFragmentDiscovery.ResolveFragmentIcon("\uE756", fragmentDir));
    }

    [Fact]
    public void load_all_reports_read_failures_for_locked_files()
    {
        var path = Path.Combine(_root, "locked", "locked.json");
        WriteFragment(
            path,
            """
            {
                "profiles": [
                    {
                        "guid": "{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}",
                        "icon": "x.ico"
                    }
                ]
            }
            """);

        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var profiles = TerminalFragmentDiscovery.LoadAll([_root], out var hadReadFailures);

        Assert.True(hadReadFailures);
        Assert.False(profiles.ContainsKey("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    }

    [Fact]
    public void load_all_malformed_json_is_not_a_read_failure()
    {
        WriteFragment(Path.Combine(_root, "broken.json"), "not json");

        var profiles = TerminalFragmentDiscovery.LoadAll([_root], out var hadReadFailures);

        Assert.False(hadReadFailures);
        Assert.Empty(profiles);
    }

    [Fact]
    public void compute_fingerprint_changes_when_file_mtime_changes()
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
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));

        var second = TerminalFragmentDiscovery.ComputeFingerprint([_root]);
        Assert.NotEqual(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(64, second.Length);
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

    [Fact]
    public void load_all_skips_non_string_guid_and_processes_rest_of_file()
    {
        WriteFragment(
            Path.Join(_root, "mixed", "mixed.json"),
            """
            {
                "profiles": [
                    {
                        "guid": "this-is-ok",
                        "commandline": "bad.exe"
                    },
                    {
                        "guid": 12345
                    },
                    {
                        "updates": 67890
                    },
                    {
                        "guid": "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}",
                        "commandline": "good.exe",
                        "icon": "good.ico"
                    }
                ]
            }
            """);

        var profiles = TerminalFragmentDiscovery.LoadAll([_root]);

        Assert.True(profiles.ContainsKey("this-is-ok"));
        Assert.DoesNotContain("12345", profiles.Keys);
        Assert.DoesNotContain("67890", profiles.Keys);
        Assert.Equal(2, profiles.Count);
         Assert.True(profiles.TryGetValue("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", out var profile));
         Assert.Equal("good.exe", profile.Commandline);
         Assert.EndsWith("good.ico", profile.Icon, StringComparison.OrdinalIgnoreCase);
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
