using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class TerminalProfileIconResolverTests : IDisposable
{
    private readonly string _root;

    public TerminalProfileIconResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Resolve_ReturnsEmojiWithoutFileLookup()
    {
        var resolved = TerminalProfileIconResolver.Resolve("🐧", Path.Combine(_root, "settings.json"));

        Assert.Equal("🐧", resolved);
    }

    [Fact]
    public void Resolve_ReturnsAbsolutePathWhenFileExists()
    {
        var iconPath = Path.Combine(_root, "custom.png");
        File.WriteAllText(iconPath, "png");

        var resolved = TerminalProfileIconResolver.Resolve(iconPath, Path.Combine(_root, "settings.json"));

        Assert.Equal(iconPath, resolved);
    }

    [Fact]
    public void Resolve_ReturnsPathRelativeToSettingsDirectory()
    {
        var settingsPath = Path.Combine(_root, "LocalState", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{}");
        var iconPath = Path.Combine(_root, "LocalState", "my-icon.png");
        File.WriteAllText(iconPath, "png");

        var resolved = TerminalProfileIconResolver.Resolve("my-icon.png", settingsPath);

        Assert.Equal(iconPath, resolved);
    }

    [Fact]
    public void Resolve_ReturnsScaledMsAppxPathWhenBaseNameMissing()
    {
        var installRoot = Path.Combine(_root, "install");
        var iconPath = Path.Combine(installRoot, "ProfileIcons", "vs-pwsh.scale-100.png");
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        File.WriteAllText(iconPath, "png");

        var resolved = TerminalProfileIconResolver.Resolve(
            "ms-appx:///ProfileIcons/vs-pwsh.png",
            Path.Combine(_root, "settings.json"),
            [installRoot]);

        Assert.Equal(iconPath, resolved);
    }

    [Fact]
    public void ResolveEffectiveIcon_UsesExplicitProfileIconPath()
    {
        var iconPath = Path.Combine(_root, "nu.ico");
        File.WriteAllText(iconPath, "ico");
        var profile = new WtProfileInfo
        {
            Name = "Nushell",
            Guid = "{47302f9c-1ac4-566c-aa3e-8cf29889d6ab}",
            Icon = iconPath,
            SettingsPath = Path.Combine(_root, "LocalState", "settings.json"),
            Source = TerminalSettingsSource.IntelligentTerminal,
            HostExecutable = "wtai.exe",
            IdPrefix = "it",
            SourceLabel = "Intelligent Terminal",
        };

        var resolved = TerminalProfileIconResolver.ResolveEffectiveIcon(profile);

        Assert.Equal(iconPath, resolved);
    }

    [Fact]
    public void Resolve_ReturnsMsAppDataRoamingPath()
    {
        var packageRoot = Path.Combine(_root, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe");
        var settingsPath = Path.Combine(packageRoot, "LocalState", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{}");
        var iconPath = Path.Combine(packageRoot, "RoamingState", "ubuntu.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        File.WriteAllText(iconPath, "ico");

        var resolved = TerminalProfileIconResolver.Resolve(
            "ms-appdata:///Roaming/ubuntu.ico",
            settingsPath);

        Assert.Equal(iconPath, resolved);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenPathDoesNotExist()
    {
        var resolved = TerminalProfileIconResolver.Resolve(
            "C:\\missing\\icon.png",
            Path.Combine(_root, "settings.json"));

        Assert.Null(resolved);
    }

    [Fact]
    public void IsCmdPalGlyphIcon_AcceptsEmojiAndRejectsFilePaths()
    {
        Assert.True(TerminalProfileIconResolver.IsCmdPalGlyphIcon("🐧"));
        Assert.True(TerminalProfileIconResolver.IsCmdPalGlyphIcon("\uE756"));
        Assert.False(TerminalProfileIconResolver.IsCmdPalGlyphIcon(@"C:\Apps\wt\ProfileIcons\debian.png"));
        Assert.False(TerminalProfileIconResolver.IsCmdPalGlyphIcon("ms-appx:///ProfileIcons/foo.png"));
    }

    [Fact]
    public void IsWslProfile_DetectsWslCommandLine()
    {
        var profile = CreateProfile("Debian", "wsl.exe -d Debian");
        Assert.True(TerminalLaunchGlyphs.IsWslProfile(profile));
    }

    [Fact]
    public void GetForLaunch_WslProfile_UsesLinuxPenguinWhenOnlyPngIconExists()
    {
        var launch = new WorkspaceEntry
        {
            Id = "1",
            Label = "Main",
            Terminal = "wt",
            WtProfile = "Debian",
        };

        var glyph = TerminalLaunchGlyphs.GetForLaunch(launch);
        Assert.Equal(ShortcutGlyphs.Linux, glyph);
    }

    private static WtProfileInfo CreateProfile(string name, string commandline) => new()
    {
        Name = name,
        Commandline = commandline,
        SettingsPath = Path.Combine(Path.GetTempPath(), "settings.json"),
        Source = TerminalSettingsSource.WindowsTerminal,
        HostExecutable = "wt.exe",
        IdPrefix = "wt",
        SourceLabel = "Windows Terminal",
    };

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
