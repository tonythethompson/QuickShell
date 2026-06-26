using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json;

namespace QuickShell;

internal sealed class QuickShellSettingsManager
{
    private const string DefaultTerminalId = "defaultTerminal";

    private readonly Settings _settings;

    public QuickShellSettingsManager()
    {
        _settings = new Settings();

        var choices = Services.TerminalCatalog.GetSettingsChoices();
        var initial = Services.TerminalCatalog.NormalizeLaunchTargetId(LoadLegacyDefaultTerminal());

        _settings.Add(new ChoiceSetSetting(DefaultTerminalId, choices)
        {
            Label = "Default terminal",
            Description = "Used when a shortcut is set to Default. Includes every Windows Terminal profile on your PC, plus WSL and other installed shells.",
        });

        if (!choices.Any(c => c.Value.Equals(initial, StringComparison.OrdinalIgnoreCase)))
        {
            initial = choices.FirstOrDefault()?.Value ?? "wt";
        }

        _settings.Update($$"""{"{{DefaultTerminalId}}":"{{initial}}"}""");

        _settings.SettingsChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SettingsChanged;

    public ICommandSettings Settings => new QuickShellCommandSettings(_settings);

    internal Settings SettingsModel => _settings;

    public IContentPage SettingsPage => new Pages.QuickShellExtensionSettingsPage(_settings);

    public string DefaultLaunchTargetId =>
        Services.TerminalCatalog.NormalizeLaunchTargetId(_settings.GetSetting<string>(DefaultTerminalId));

    private static string LoadLegacyDefaultTerminal()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "settings.json");

        try
        {
            if (!File.Exists(legacyPath))
            {
                return "wt";
            }

            using var stream = File.OpenRead(legacyPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("DefaultTerminal", out var terminal))
            {
                return Services.TerminalCatalog.NormalizeLaunchTargetId(terminal.GetString());
            }

            return "wt";
        }
        catch
        {
            return "wt";
        }
    }
}
