using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class QuickShellSettingsLaunchTests
{
    [Theory]
    [InlineData(null, TerminalHostIds.LetWindowsChoose)]
    [InlineData("  ", TerminalHostIds.LetWindowsChoose)]
    [InlineData(" WT ", TerminalHostIds.WindowsTerminal)]
    [InlineData(" Custom-Terminal ", "custom-terminal")]
    public void NormalizeTerminalApplication_TrimsAndLowercases(
        string? value,
        string expected)
    {
        Assert.Equal(expected, QuickShellSettingsManager.NormalizeTerminalApplication(value));
    }

    [Fact]
    public void OpenWorkspace_BeforeWarmup_ValidatesUnavailableTerminalAndMissingProfile()
    {
        RunLaunchValidationTest((shortcut, services) =>
            new OpenTerminalShortcutCommand(shortcut, services).Invoke());
    }

    [Fact]
    public void OpenLaunchEntry_BeforeWarmup_ValidatesUnavailableTerminalAndMissingProfile()
    {
        RunLaunchValidationTest((shortcut, services) =>
            new OpenShortcutLaunchCommand(shortcut, shortcut.Launches[0], services).Invoke());
    }

    [Fact]
    public async Task PrewarmTerminalCatalog_DoesNotOverwriteConcurrentTerminalEdit()
    {
        var configDirectory = CreateConfigDirectory();
        using var releaseCatalog = new ManualResetEventSlim(false);
        using var prewarmCatalogStarted = new ManualResetEventSlim(false);
        using var updateCatalogStarted = new ManualResetEventSlim(false);
        var catalogCalls = 0;

        try
        {
            var settings = CreateSettings(
                configDirectory,
                () =>
                {
                    if (Interlocked.Increment(ref catalogCalls) == 1)
                    {
                        prewarmCatalogStarted.Set();
                    }
                    else
                    {
                        updateCatalogStarted.Set();
                    }

                    releaseCatalog.Wait(TimeSpan.FromSeconds(5));
                    return MinimalApplicationChoices();
                });
            settings.SettingsModel.Update("""{"terminalApplication":"it","defaultProfile":"Missing"}""");

            var prewarm = Task.Run(settings.PrewarmTerminalCatalog);
            Assert.True(prewarmCatalogStarted.Wait(TimeSpan.FromSeconds(5)));

            var update = Task.Run(() => settings.UpdateTerminalDefaults(
                TerminalHostIds.WindowsTerminal,
                TerminalHostIds.DefaultProfile));
            Assert.True(updateCatalogStarted.Wait(TimeSpan.FromSeconds(5)));
            releaseCatalog.Set();

            await Task.WhenAll(prewarm, update).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TerminalHostIds.WindowsTerminal, settings.TerminalApplicationId);
            Assert.Equal(TerminalHostIds.DefaultProfile, settings.DefaultProfileId);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    [Fact]
    public void PrewarmTerminalCatalog_DoesNotPersistFallbackDefaults()
    {
        var configDirectory = CreateConfigDirectory();
        try
        {
            var store = new QuickShellJsonSettingsStore(configDirectory);
            var settings = new QuickShellSettingsManager(
                store,
                hasTerminalApplication: _ => false,
                getTerminalApplicationChoices: MinimalApplicationChoices,
                getDefaultProfileChoices: _ =>
                [
                    new ChoiceSetSetting.Choice(
                        "Default profile for this app",
                        TerminalHostIds.DefaultProfile),
                ]);
            settings.SettingsModel.Update("""{"terminalApplication":"it","defaultProfile":"Missing"}""");
            store.SaveSettings();
            var settingsPath = Path.Join(configDirectory, "settings.json");
            var persistedBeforeWarmup = File.ReadAllText(settingsPath);

            settings.PrewarmTerminalCatalog();

            Assert.Equal(persistedBeforeWarmup, File.ReadAllText(settingsPath));
            Assert.Equal(TerminalHostIds.IntelligentTerminal, settings.TerminalApplicationId);
            Assert.Equal("Missing", settings.DefaultProfileId);
            Assert.Equal(
                (TerminalHostIds.WindowsTerminal, TerminalHostIds.DefaultProfile),
                settings.GetValidatedLaunchDefaults());
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    private static void RunLaunchValidationTest(
        Func<TerminalShortcut, QuickShellServices, object> invoke)
    {
        var configDirectory = CreateConfigDirectory();
        try
        {
            var settings = CreateSettings(configDirectory);
            settings.SettingsModel.Update("""{"terminalApplication":"it","defaultProfile":"Missing"}""");

            var shortcut = new TerminalShortcut
            {
                Id = "launch-validation",
                Name = "Launch validation",
                Directory = configDirectory,
                Launches =
                [
                    new WorkspaceEntry
                    {
                        Id = "default-entry",
                        Label = "Default",
                        Terminal = "default",
                        IsEnabled = true,
                    },
                ],
            };
            var repository = new FakeShortcutRepository([shortcut]);
            var launch = LaunchTestServices.CreateBundle();
            var services = TestQuickShellServicesFactory.Create(
                repository,
                new ShortcutDraftStore(repository),
                settings,
                new FakeProjectAnalysisService(),
                new QuickShellLifetime(),
                launch);

            _ = invoke(shortcut, services);

            var started = Assert.Single(launch.ProcessStarter.Started);
            Assert.Equal("wt.exe", started.FileName);
            Assert.DoesNotContain("Missing", started.Arguments, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
        }
    }

    private static QuickShellSettingsManager CreateSettings(
        string configDirectory,
        Func<List<ChoiceSetSetting.Choice>>? getTerminalApplicationChoices = null) =>
        new(
            new QuickShellJsonSettingsStore(configDirectory),
            hasTerminalApplication: _ => false,
            getTerminalApplicationChoices: getTerminalApplicationChoices ?? MinimalApplicationChoices,
            getDefaultProfileChoices: _ =>
            [
                new ChoiceSetSetting.Choice(
                    "Default profile for this app",
                    TerminalHostIds.DefaultProfile),
            ]);

    private static List<ChoiceSetSetting.Choice> MinimalApplicationChoices() =>
    [
        new ChoiceSetSetting.Choice("Windows Terminal", TerminalHostIds.WindowsTerminal),
    ];

    private static string CreateConfigDirectory()
    {
        var path = Path.Join(
            Path.GetTempPath(),
            "qs-launch-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
