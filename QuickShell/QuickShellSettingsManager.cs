using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Text.Json;

namespace QuickShell;

internal sealed class QuickShellSettingsManager
{
    private const string TerminalApplicationSettingId = "terminalApplication";
    private const string DefaultProfileSettingId = "defaultProfile";

    private readonly QuickShellJsonSettingsStore _settingsStore;
    private readonly Settings _settings;
    private readonly ChoiceSetSetting _terminalApplicationSetting;
    private readonly ChoiceSetSetting _defaultProfileSetting;
    private readonly Pages.QuickShellExtensionSettingsPage _settingsPage;

    public QuickShellSettingsManager(Action? onReload = null)
    {
        _settingsStore = new QuickShellJsonSettingsStore();
        _settings = _settingsStore.Settings;

        _terminalApplicationSetting = new ChoiceSetSetting(
            TerminalApplicationSettingId,
            TerminalCatalog.GetTerminalApplicationChoices())
        {
            Label = "Terminal application",
            Description = "The terminal host used for Default shortcuts and profile launches. Matches Windows Terminal's \"Default terminal application\" setting.",
        };

        _defaultProfileSetting = new ChoiceSetSetting(
            DefaultProfileSettingId,
            TerminalCatalog.GetDefaultProfileChoices(TerminalHostIds.WindowsTerminal))
        {
            Label = "Default profile",
            Description = "Profile used when a shortcut is set to Default. Per-shortcut profile choices stay on each shortcut.",
        };

        _settings.Add(_terminalApplicationSetting);
        _settings.Add(_defaultProfileSetting);
        _settingsStore.LoadSettings();

        var usedLegacyDefaults = false;
        var initialApp = _settings.GetSetting<string>(TerminalApplicationSettingId);
        var initialProfile = _settings.GetSetting<string>(DefaultProfileSettingId);

        if (string.IsNullOrWhiteSpace(initialApp))
        {
            (initialApp, initialProfile) = LoadLegacyTerminalDefaults();
            usedLegacyDefaults = true;
        }

        initialApp = EnsureValidTerminalApplication(initialApp);
        _defaultProfileSetting.Choices = TerminalCatalog.GetDefaultProfileChoices(initialApp);
        initialProfile = EnsureValidDefaultProfile(initialApp, initialProfile);

        _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{initialApp}}","{{DefaultProfileSettingId}}":"{{initialProfile}}"}""");

        if (usedLegacyDefaults || !File.Exists(_settingsStore.FilePath))
        {
            _settingsStore.SaveSettings();
        }

        _settingsPage = new Pages.QuickShellExtensionSettingsPage(this, onReload);
    }

    public event EventHandler? SettingsChanged;

    public ICommandSettings Settings => new QuickShellCommandSettings(_settings, _settingsPage);

    internal Settings SettingsModel => _settings;

    internal void RefreshSettingsContent() => _settingsPage.RefreshContent();

    public IContentPage SettingsPage => _settingsPage;

    public string TerminalApplicationId =>
        EnsureValidTerminalApplication(_settings.GetSetting<string>(TerminalApplicationSettingId));

    public string DefaultProfileId =>
        EnsureValidDefaultProfile(TerminalApplicationId, _settings.GetSetting<string>(DefaultProfileSettingId));

    internal void UpdateTerminalDefaults(string app, string profile)
    {
        app = EnsureValidTerminalApplication(app);
        profile = EnsureValidDefaultProfile(app, profile);
        _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{EscapeJson(app)}}","{{DefaultProfileSettingId}}":"{{EscapeJson(profile)}}"}""");
        RefreshTerminalChoices();
        PersistSettings();
    }

    internal void PersistSettings()
    {
        _settingsStore.SaveSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshTerminalChoices()
    {
        var app = TerminalApplicationId;
        _terminalApplicationSetting.Choices = TerminalCatalog.GetTerminalApplicationChoices();
        app = EnsureValidTerminalApplication(app);
        SyncDefaultProfileChoices();
        _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{app}}","{{DefaultProfileSettingId}}":"{{DefaultProfileId}}"}""");
        PersistSettings();
    }

    private void SyncDefaultProfileChoices()
    {
        var app = EnsureValidTerminalApplication(_settings.GetSetting<string>(TerminalApplicationSettingId));
        _defaultProfileSetting.Choices = TerminalCatalog.GetDefaultProfileChoices(app);

        var current = _settings.GetSetting<string>(DefaultProfileSettingId);
        if (!_defaultProfileSetting.Choices.Any(c => c.Value.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.Update($$"""{"{{DefaultProfileSettingId}}":"{{TerminalHostIds.DefaultProfile}}"}""");
        }
    }

    private static string EnsureValidTerminalApplication(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TerminalHostIds.LetWindowsChoose
            : value.Trim().ToLowerInvariant();

        if (normalized.Equals(TerminalHostIds.LetWindowsChoose, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.LetWindowsChoose;
        }

        if (normalized.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.WindowsConsoleHost;
        }

        if (normalized.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            && TerminalCatalog.HasTerminalApplication(TerminalHostIds.IntelligentTerminal))
        {
            return TerminalHostIds.IntelligentTerminal;
        }

        return TerminalHostIds.WindowsTerminal;
    }

    private string EnsureValidDefaultProfile(string terminalApplicationId, string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TerminalHostIds.DefaultProfile
            : value.Trim();

        if (normalized.Equals(TerminalHostIds.DefaultProfile, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalHostIds.DefaultProfile;
        }

        if (TerminalCatalog.IsStandaloneShellLaunchTarget(normalized))
        {
            return normalized;
        }

        if (TryExtractProfileName(normalized, out var profileName)
            && TerminalCatalog.GetDefaultProfileChoices(terminalApplicationId)
                .Any(c => c.Value.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
        {
            return profileName;
        }

        if (_defaultProfileSetting.Choices.Any(c => c.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return TerminalHostIds.DefaultProfile;
    }

    private static (string App, string Profile) LoadLegacyTerminalDefaults()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickShell",
            "settings.json");

        var legacyValue = LoadLegacyDefaultTerminal(legacyPath);
        return MigrateLegacyDefaultTerminal(legacyValue);
    }

    private static (string App, string Profile) MigrateLegacyDefaultTerminal(string legacy)
    {
        var value = TerminalCatalog.NormalizeLaunchTargetId(legacy);

        if (value.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("it:", StringComparison.OrdinalIgnoreCase))
        {
            return (
                TerminalHostIds.IntelligentTerminal,
                value.StartsWith("it:", StringComparison.OrdinalIgnoreCase) ? value[3..] : TerminalHostIds.DefaultProfile);
        }

        if (value.StartsWith("wt:", StringComparison.OrdinalIgnoreCase))
        {
            return (TerminalHostIds.WindowsTerminal, value[3..]);
        }

        if (TerminalCatalog.IsStandaloneShellLaunchTarget(value))
        {
            return (TerminalHostIds.WindowsTerminal, value);
        }

        return (TerminalHostIds.WindowsTerminal, TerminalHostIds.DefaultProfile);
    }

    private static bool TryExtractProfileName(string value, out string profileName)
    {
        if (value.StartsWith("wt:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("it:", StringComparison.OrdinalIgnoreCase))
        {
            profileName = value[(value.IndexOf(':') + 1)..];
            return !string.IsNullOrWhiteSpace(profileName);
        }

        profileName = string.Empty;
        return false;
    }

    private static string LoadLegacyDefaultTerminal(string legacyPath)
    {
        try
        {
            if (!File.Exists(legacyPath))
            {
                return TerminalHostIds.WindowsTerminal;
            }

            using var stream = File.OpenRead(legacyPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("DefaultTerminal", out var terminal))
            {
                return TerminalCatalog.NormalizeLaunchTargetId(terminal.GetString());
            }

            return TerminalHostIds.WindowsTerminal;
        }
        catch
        {
            return TerminalHostIds.WindowsTerminal;
        }
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
