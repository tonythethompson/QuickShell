using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CompanionAppNormalizationTests
{
    [Fact]
    public void EnsureCompanionsFromLegacy_SynthesizesSingleEntryFromScalars()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Legacy",
            Directory = @"C:\Projects\App",
            OpenCompanionAppOnLaunch = true,
            CompanionAppPath = @"C:\Apps\Code.exe",
            CompanionAppArguments = ".",
        };

        CompanionAppNormalization.EnsureCompanionsFromLegacy(shortcut);

        Assert.Single(shortcut.CompanionApps);
        Assert.Equal(@"C:\Apps\Code.exe", shortcut.CompanionApps[0].Path);
        Assert.Equal(".", shortcut.CompanionApps[0].Arguments);
        Assert.True(shortcut.CompanionApps[0].OpenOnLaunch);
        Assert.Equal(0, shortcut.CompanionApps[0].Order);
        Assert.False(string.IsNullOrWhiteSpace(shortcut.CompanionApps[0].Id));
    }

    [Fact]
    public void NormalizeCompanions_MirrorsPrimaryBackToLegacyScalars()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Multi",
            Directory = @"C:\Projects\App",
            CompanionApps =
            [
                new CompanionAppEntry
                {
                    Id = "a",
                    Path = @"C:\Apps\Code.exe",
                    Arguments = ".",
                    OpenOnLaunch = true,
                    Order = 1,
                },
                new CompanionAppEntry
                {
                    Id = "b",
                    Path = @"C:\Apps\Fork.exe",
                    Arguments = "{folder}",
                    OpenOnLaunch = false,
                    Order = 0,
                },
            ],
        };

        CompanionAppNormalization.NormalizeCompanions(shortcut);

        Assert.Equal(2, shortcut.CompanionApps.Count);
        Assert.Equal(@"C:\Apps\Fork.exe", shortcut.CompanionApps[0].Path);
        Assert.Equal(0, shortcut.CompanionApps[0].Order);
        Assert.Equal(1, shortcut.CompanionApps[1].Order);
        // Primary (first by order) mirrors to legacy fields.
        Assert.Equal(@"C:\Apps\Fork.exe", shortcut.CompanionAppPath);
        Assert.Equal("{folder}", shortcut.CompanionAppArguments);
        Assert.False(shortcut.OpenCompanionAppOnLaunch);
    }

    [Fact]
    public void ApplyPrimaryFromScalars_PreservesAdditionalCompanions()
    {
        var existing = new List<CompanionAppEntry>
        {
            new()
            {
                Id = "primary",
                Path = @"C:\Apps\Old.exe",
                Arguments = ".",
                OpenOnLaunch = true,
                Order = 0,
            },
            new()
            {
                Id = "extra",
                Path = @"C:\Apps\Fork.exe",
                Arguments = "{folder}",
                OpenOnLaunch = true,
                Order = 1,
            },
        };

        var shortcut = new TerminalShortcut
        {
            Name = "Edit",
            Directory = @"C:\Projects\App",
        };

        CompanionAppNormalization.ApplyPrimaryFromScalars(
            shortcut,
            openOnLaunch: false,
            path: @"C:\Apps\Code.exe",
            arguments: ".",
            preserveAdditionalFrom: existing);

        Assert.Equal(2, shortcut.CompanionApps.Count);
        Assert.Equal("primary", shortcut.CompanionApps[0].Id);
        Assert.Equal(@"C:\Apps\Code.exe", shortcut.CompanionApps[0].Path);
        Assert.False(shortcut.CompanionApps[0].OpenOnLaunch);
        Assert.Equal("extra", shortcut.CompanionApps[1].Id);
        Assert.Equal(@"C:\Apps\Fork.exe", shortcut.CompanionApps[1].Path);
        Assert.True(shortcut.CompanionApps[1].OpenOnLaunch);
        Assert.Equal(@"C:\Apps\Code.exe", shortcut.CompanionAppPath);
        Assert.False(shortcut.OpenCompanionAppOnLaunch);
    }

    [Fact]
    public void ApplyPrimaryFromScalars_ClearingPrimaryKeepsAdditional()
    {
        var existing = new List<CompanionAppEntry>
        {
            new() { Id = "p", Path = @"C:\Apps\Code.exe", OpenOnLaunch = true, Order = 0 },
            new() { Id = "e", Path = @"C:\Apps\Fork.exe", OpenOnLaunch = true, Order = 1 },
        };

        var shortcut = new TerminalShortcut { Name = "Edit", Directory = @"C:\Projects\App" };
        CompanionAppNormalization.ApplyPrimaryFromScalars(
            shortcut,
            openOnLaunch: false,
            path: null,
            arguments: null,
            preserveAdditionalFrom: existing);

        Assert.Single(shortcut.CompanionApps);
        Assert.Equal("e", shortcut.CompanionApps[0].Id);
        Assert.Equal(@"C:\Apps\Fork.exe", shortcut.CompanionAppPath);
        Assert.True(shortcut.OpenCompanionAppOnLaunch);
    }

    [Fact]
    public void NormalizeCompanions_CapsAtMaxCount()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "Many",
            Directory = @"C:\Projects\App",
            CompanionApps = Enumerable.Range(0, CompanionAppNormalization.MaxCompanionCount + 3)
                .Select(i => new CompanionAppEntry
                {
                    Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Path = $@"C:\Apps\App{i}.exe",
                    OpenOnLaunch = i == 0,
                    Order = i,
                })
                .ToList(),
        };

        CompanionAppNormalization.NormalizeCompanions(shortcut);

        Assert.Equal(CompanionAppNormalization.MaxCompanionCount, shortcut.CompanionApps.Count);
    }

    [Fact]
    public void NormalizeShortcut_SynthesizesCompanionsFromLegacy()
    {
        var shortcut = new TerminalShortcut
        {
            Name = "ViaNormalize",
            Directory = @"C:\Projects\App",
            Command = "npm start",
            CompanionAppPath = "explorer.exe",
            OpenCompanionAppOnLaunch = true,
        };

        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        Assert.Single(shortcut.CompanionApps);
        Assert.Equal("explorer.exe", shortcut.CompanionApps[0].Path);
        Assert.True(shortcut.CompanionApps[0].OpenOnLaunch);
    }
}
