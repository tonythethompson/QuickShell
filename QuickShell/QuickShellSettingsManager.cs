using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;
using System.Globalization;
using System.Text.Json;

namespace QuickShell;

internal sealed class QuickShellSettingsManager
{
    private const string TerminalApplicationSettingId = "terminalApplication";
    private const string DefaultProfileSettingId = "defaultProfile";
    private const string RecentWorkspaceCountSettingId = QuickShellRecentSettings.SettingKey;
    private const string BlockDirtyBranchSwitchSettingId = "blockDirtyBranchSwitch";
    private const string MultiLaunchPresentationSettingId = QuickShellMultiLaunchSettings.SettingKey;

    private readonly QuickShellJsonSettingsStore _settingsStore;
    private readonly Settings _settings;
    private readonly ChoiceSetSetting _terminalApplicationSetting;
    private readonly ChoiceSetSetting _defaultProfileSetting;
    private readonly TextSetting _recentWorkspaceCountSetting;
    private readonly TextSetting _blockDirtyBranchSwitchSetting;
    private readonly TextSetting _multiLaunchPresentationSetting;
    private readonly object _terminalDefaultsSync = new();
    private Pages.QuickShellExtensionSettingsPage? _settingsPage;
    private readonly Action? _onReload;
    private IQuickShellServices _quickShellServices = null!;
    private bool _servicesInitialized;

    internal IQuickShellServices Services
    {
        get => _quickShellServices ?? throw new InvalidOperationException("IQuickShellServices must be set before accessing settings UI.");
        private set
        {
            if (_servicesInitialized && !ReferenceEquals(_quickShellServices, value))
            {
                throw new InvalidOperationException("IQuickShellServices has already been initialized and cannot be reassigned.");
            }

            _quickShellServices = value;
            _servicesInitialized = true;
        }
    }

    internal void InitializeServices(IQuickShellServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public QuickShellSettingsManager(Action? onReload = null)
    {
        _onReload = onReload;
        // #region agent log
        SupportDiagnostics.Write("QuickShellSettingsManager.cs:ctor", "start", hypothesisId: "A");
        // #endregion

        _settingsStore = new QuickShellJsonSettingsStore();
        _settings = _settingsStore.Settings;

        // Choices are populated by PrewarmTerminalCatalog after the first workspace list
        // is published so terminal discovery (WT settings, vswhere, PATH) does not run
        // during provider construction.
        _terminalApplicationSetting = new ChoiceSetSetting(
            TerminalApplicationSettingId,
            new List<ChoiceSetSetting.Choice>
            {
                new("Let Windows choose", TerminalHostIds.LetWindowsChoose),
            })
        {
            Label = "Terminal application",
            Description = "The terminal host used for Default workspaces and profile launches. Matches Windows Terminal's \"Default terminal application\" setting.",
        };

        _defaultProfileSetting = new ChoiceSetSetting(
            DefaultProfileSettingId,
            new List<ChoiceSetSetting.Choice>
            {
                new("Default profile for this app", TerminalHostIds.DefaultProfile),
            })
        {
            Label = "Default profile",
            Description = "Profile used when a workspace is set to Default. Per-workspace profile choices stay on each workspace.",
        };

        _recentWorkspaceCountSetting = new TextSetting(
            RecentWorkspaceCountSettingId,
            "Show recent workspaces",
            $"On/off toggle, not a count: any non-zero value shows the {QuickShellRecentSettings.EnabledCount} most recently used workspaces on the home page; 0 hides the section.",
            QuickShellRecentSettings.DefaultCount.ToString(CultureInfo.InvariantCulture));

        _blockDirtyBranchSwitchSetting = new TextSetting(
            BlockDirtyBranchSwitchSettingId,
            "Block launch when dirty and branch would change",
            "When a worktree target branch differs from HEAD, block launch and branch switching if the working tree has uncommitted changes.",
            "true");

        _multiLaunchPresentationSetting = new TextSetting(
            MultiLaunchPresentationSettingId,
            "Multi-command launch",
            "Whether multi-command workspaces open as tabs in one window or as separate windows.",
            QuickShellMultiLaunchSettings.SingleWindowTabs);

        _settings.Add(_terminalApplicationSetting);
        _settings.Add(_defaultProfileSetting);
        _settings.Add(_recentWorkspaceCountSetting);
        _settings.Add(_blockDirtyBranchSwitchSetting);
        _settings.Add(_multiLaunchPresentationSetting);
        _settingsStore.LoadSettings();

        // #region agent log
        SupportDiagnostics.Write(
            "QuickShellSettingsManager.cs:ctor",
            "after LoadSettings",
            new { settingsPath = _settingsStore.FilePath, exists = File.Exists(_settingsStore.FilePath) },
            hypothesisId: "A");
        // #endregion

        var usedLegacyDefaults = false;
        var initialApp = _settings.GetSetting<string>(TerminalApplicationSettingId);
        var initialProfile = _settings.GetSetting<string>(DefaultProfileSettingId);

        if (string.IsNullOrWhiteSpace(initialApp))
        {
            (initialApp, initialProfile) = LoadLegacyTerminalDefaults();
            usedLegacyDefaults = true;
        }

        initialApp = NormalizeTerminalApplication(initialApp);
        initialProfile = NormalizeStoredDefaultProfile(initialProfile);
        var initialRecentCount = ReadRecentWorkspaceCount();
        var initialBlockDirtyBranchSwitch = ReadBlockDirtyBranchSwitch();
        var initialMultiLaunchPresentation = ReadMultiLaunchPresentation();

        _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{initialApp}}","{{DefaultProfileSettingId}}":"{{initialProfile}}","{{RecentWorkspaceCountSettingId}}":"{{QuickShellRecentSettings.FormatCount(initialRecentCount)}}","{{BlockDirtyBranchSwitchSettingId}}":"{{FormatBool(initialBlockDirtyBranchSwitch)}}","{{MultiLaunchPresentationSettingId}}":"{{initialMultiLaunchPresentation}}"}""");

        // Terminal/profile catalog choices and final validation are deferred to the
        // staged startup coordinator so provider construction stays off the hot path.
        // #region agent log
        SupportDiagnostics.Write(
            "QuickShellSettingsManager.cs:ctor",
            "terminal defaults deferred",
            new { initialApp, initialProfile },
            hypothesisId: "C");
        // #endregion

        if (usedLegacyDefaults || !File.Exists(_settingsStore.FilePath))
        {
            _settingsStore.SaveSettings();
        }

        // #region agent log
        SupportDiagnostics.Write("QuickShellSettingsManager.cs:ctor", "complete", hypothesisId: "A");
        // #endregion
    }

    public event EventHandler? SettingsChanged;

    public ICommandSettings Settings => new QuickShellCommandSettings(_settings, SettingsPage);

    internal Settings SettingsModel => _settings;

    internal Pages.QuickShellExtensionSettingsPage TypedSettingsPage => _settingsPage ??= new Pages.QuickShellExtensionSettingsPage(this, Services, _onReload);

    public IContentPage SettingsPage => TypedSettingsPage;

    internal void RefreshSettingsContent() => TypedSettingsPage.RefreshContent();

    internal void PrewarmSettingsContent() => TypedSettingsPage.PrewarmContent();

    /// <summary>
    /// Returns the configured terminal application without forcing a catalog scan.
    /// Validation and choice population are performed by <see cref="PrewarmTerminalCatalog"/>.
    /// </summary>
    public string TerminalApplicationId =>
        NormalizeTerminalApplication(_settings.GetSetting<string>(TerminalApplicationSettingId));

    /// <summary>
    /// Returns the configured default profile without forcing a catalog scan.
    /// Validation and choice population are performed by <see cref="PrewarmTerminalCatalog"/>.
    /// </summary>
    public string DefaultProfileId =>
        NormalizeStoredDefaultProfile(_settings.GetSetting<string>(DefaultProfileSettingId));

    public int RecentWorkspaceCount => ReadRecentWorkspaceCount();

    public bool BlockDirtyBranchSwitch => ReadBlockDirtyBranchSwitch();

    public bool SeparateWindowsForMultiLaunch =>
        QuickShellMultiLaunchSettings.IsSeparateWindows(ReadMultiLaunchPresentation());

    /// <summary>
    /// Validates terminal defaults at the point they are consumed for a launch.
    /// Returns a (terminal application, default profile) pair with stale values
    /// (e.g. a removed terminal host) substituted for safe fallbacks. Reads under
    /// a lock so concurrent settings writes cannot interleave between the two reads.
    /// </summary>
    internal (string TerminalApplicationId, string DefaultProfileId) GetValidatedLaunchDefaults()
    {
        lock (_terminalDefaultsSync)
        {
            var app = EnsureValidTerminalApplication(_settings.GetSetting<string>(TerminalApplicationSettingId));
            var profile = EnsureValidDefaultProfile(app, _settings.GetSetting<string>(DefaultProfileSettingId));
            return (app, profile);
        }
    }

    internal void UpdateTerminalDefaults(string app, string profile)
    {
        lock (_terminalDefaultsSync)
        {
            app = EnsureValidTerminalApplication(app);
            profile = EnsureValidDefaultProfile(app, profile);
            _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{EscapeJson(app)}}","{{DefaultProfileSettingId}}":"{{EscapeJson(profile)}}"}""");
            _terminalApplicationSetting.Choices = TerminalCatalogChoices.GetTerminalApplicationChoices();
            SyncDefaultProfileChoices();
            PersistSettings();
        }
    }

    internal void UpdateRecentWorkspaceCount(int count)
    {
        count = QuickShellRecentSettings.NormalizeCount(count);
        _settings.Update($$"""{"{{RecentWorkspaceCountSettingId}}":"{{QuickShellRecentSettings.FormatCount(count)}}"}""");
        PersistSettings();
    }

    internal void UpdateBlockDirtyBranchSwitch(bool enabled)
    {
        _settings.Update($$"""{"{{BlockDirtyBranchSwitchSettingId}}":"{{FormatBool(enabled)}}"}""");
        PersistSettings();
    }

    internal void UpdateMultiLaunchPresentation(bool singleWindowTabs)
    {
        var value = singleWindowTabs
            ? QuickShellMultiLaunchSettings.SingleWindowTabs
            : QuickShellMultiLaunchSettings.SeparateWindows;
        _settings.Update($$"""{"{{MultiLaunchPresentationSettingId}}":"{{value}}"}""");
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
        _terminalApplicationSetting.Choices = TerminalCatalogChoices.GetTerminalApplicationChoices();
        app = EnsureValidTerminalApplication(app);
        SyncDefaultProfileChoices();
        _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{app}}","{{DefaultProfileSettingId}}":"{{DefaultProfileId}}"}""");
        PersistSettings();
    }

    /// <summary>
    /// Warms the terminal/profile catalogs and finalizes the default terminal settings.
    /// Called by the staged startup coordinator after the first workspace list is published
    /// so provider construction does not pay the discovery cost.
    /// </summary>
    internal void PrewarmTerminalCatalog()
    {
        var app = EnsureValidTerminalApplication(_settings.GetSetting<string>(TerminalApplicationSettingId));
        var profile = EnsureValidDefaultProfile(app, _settings.GetSetting<string>(DefaultProfileSettingId));

        _terminalApplicationSetting.Choices = TerminalCatalogChoices.GetTerminalApplicationChoices();
        _defaultProfileSetting.Choices = TerminalCatalogChoices.GetDefaultProfileChoices(app);

        _settings.Update($$"""{"{{TerminalApplicationSettingId}}":"{{EscapeJson(app)}}","{{DefaultProfileSettingId}}":"{{EscapeJson(profile)}}"}""");
        _settingsStore.SaveSettings();
    }

    private void SyncDefaultProfileChoices()
    {
        var app = EnsureValidTerminalApplication(_settings.GetSetting<string>(TerminalApplicationSettingId));
        _defaultProfileSetting.Choices = TerminalCatalogChoices.GetDefaultProfileChoices(app);

        var current = _settings.GetSetting<string>(DefaultProfileSettingId);
        if (!_defaultProfileSetting.Choices.Any(c => c.Value.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.Update($$"""{"{{DefaultProfileSettingId}}":"{{TerminalHostIds.DefaultProfile}}"}""");
        }
    }

    private static string NormalizeTerminalApplication(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? TerminalHostIds.LetWindowsChoose
            : value.Trim().ToLowerInvariant();

        if (normalized.Equals(TerminalHostIds.LetWindowsChoose, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(TerminalHostIds.WindowsConsoleHost, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(TerminalHostIds.IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(TerminalHostIds.WindowsTerminal, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return normalized;
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

    private static string EnsureValidDefaultProfile(string terminalApplicationId, string? value)
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
            && TerminalCatalogChoices.GetDefaultProfileChoices(terminalApplicationId)
                .Any(c => c.Value.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
        {
            return profileName;
        }

        if (TerminalCatalogChoices.GetDefaultProfileChoices(terminalApplicationId)
                .Any(c => c.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return TerminalHostIds.DefaultProfile;
    }

    private static string NormalizeStoredDefaultProfile(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? TerminalHostIds.DefaultProfile
            : value.Trim();

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

    private int ReadRecentWorkspaceCount()
    {
        var raw = _settings.GetSetting<string>(RecentWorkspaceCountSettingId);
        return QuickShellRecentSettings.TryParseCount(raw, out var parsed)
            ? parsed
            : QuickShellRecentSettings.DefaultCount;
    }

    private bool ReadBlockDirtyBranchSwitch()
    {
        var raw = _settings.GetSetting<string>(BlockDirtyBranchSwitchSettingId);
        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    private string ReadMultiLaunchPresentation()
    {
        var raw = _settings.GetSetting<string>(MultiLaunchPresentationSettingId);
        return QuickShellMultiLaunchSettings.Normalize(raw);
    }

    private static string FormatBool(bool value) => value ? "true" : "false";
}
