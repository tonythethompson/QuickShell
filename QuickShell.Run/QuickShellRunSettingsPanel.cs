using Microsoft.PowerToys.Settings.UI.Library;
using QuickShell.Services;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;

namespace QuickShell.Run;

internal sealed class QuickShellRunSettingsPanel : UserControl
{
    private readonly QuickShellSettingsReader _settings;
    private readonly ShortcutRepository _shortcuts;
    private readonly Action<string, string> _onDefaultsSaved;
    private readonly ComboBox _terminalAppBox;
    private readonly ComboBox _defaultProfileBox;
    private readonly TextBlock _statusText;

    public QuickShellRunSettingsPanel(
        QuickShellSettingsReader settings,
        ShortcutRepository shortcuts,
        Action<string, string> onDefaultsSaved)
    {
        _settings = settings;
        _shortcuts = shortcuts;
        _onDefaultsSaved = onDefaultsSaved;

        var root = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };

        root.Children.Add(new TextBlock
        {
            Text = "Terminal defaults",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        root.Children.Add(new TextBlock { Text = "Terminal application", Margin = new Thickness(0, 0, 0, 4) });
        _terminalAppBox = new ComboBox
        {
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 0, 8),
        };
        foreach (var choice in RunTerminalChoices.GetTerminalApplicationChoices())
        {
            _terminalAppBox.Items.Add(new { choice.Id, choice.Label });
        }

        _terminalAppBox.SelectionChanged += (_, _) => ReloadProfileChoices();
        root.Children.Add(_terminalAppBox);

        root.Children.Add(new TextBlock { Text = "Default profile", Margin = new Thickness(0, 0, 0, 4) });
        _defaultProfileBox = new ComboBox
        {
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(_defaultProfileBox);

        var saveDefaults = new Button
        {
            Content = "Save terminal defaults",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16),
        };
        saveDefaults.Click += (_, _) => SaveDefaults();
        root.Children.Add(saveDefaults);

        root.Children.Add(new TextBlock
        {
            Text = "Shortcuts",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        root.Children.Add(CreateButton("Create shortcut", () => ShortcutEditor.TryShowDialog(null, _shortcuts, out _)));
        root.Children.Add(CreateButton("Export shortcuts…", ExportShortcuts));
        root.Children.Add(CreateButton("Import and merge…", () => ImportShortcuts(replace: false)));
        root.Children.Add(CreateButton("Import and replace all…", () => ImportShortcuts(replace: true)));
        root.Children.Add(CreateButton("Open shortcuts.json", () => RunFileDialogs.OpenPathInEditor(_shortcuts.ConfigPath)));
        root.Children.Add(CreateButton("Open Quick Shell data folder", () => RunFileDialogs.OpenFolder(_shortcuts.ConfigDirectory)));

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = System.Windows.Media.Brushes.Gray,
        };
        root.Children.Add(_statusText);

        Content = root;
        LoadCurrentDefaults();
    }

    public void Reload()
    {
        LoadCurrentDefaults();
        SetStatus(string.Empty);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method reserved for future settings-panel wiring.")]
    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        _ = settings;
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            MinWidth = 220,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void LoadCurrentDefaults()
    {
        _terminalAppBox.SelectedValue = _settings.TerminalApplicationId;
        ReloadProfileChoices();
        _defaultProfileBox.SelectedValue = _settings.DefaultProfileId;
    }

    private void ReloadProfileChoices()
    {
        var app = _terminalAppBox.SelectedValue as string ?? TerminalHostIds.WindowsTerminal;
        var selected = _defaultProfileBox.SelectedValue as string ?? _settings.DefaultProfileId;
        _defaultProfileBox.Items.Clear();
        foreach (var choice in RunTerminalChoices.GetDefaultProfileChoices(app))
        {
            _defaultProfileBox.Items.Add(new { choice.Id, choice.Label });
        }

        _defaultProfileBox.SelectedValue = RunTerminalChoices.GetDefaultProfileChoices(app)
            .Any(choice => choice.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))
            ? selected
            : TerminalHostIds.DefaultProfile;
    }

    private void SaveDefaults()
    {
        var app = _terminalAppBox.SelectedValue as string ?? TerminalHostIds.LetWindowsChoose;
        var profile = _defaultProfileBox.SelectedValue as string ?? TerminalHostIds.DefaultProfile;
        _settings.SaveTerminalDefaults(app, profile);
        _onDefaultsSaved(app, profile);
        SetStatus("Saved terminal defaults.");
    }

    private void ExportShortcuts()
    {
        if (RunFileDialogs.TryExportShortcuts(_shortcuts, Window.GetWindow(this), out var message))
        {
            SetStatus(message);
        }
    }

    private void ImportShortcuts(bool replace)
    {
        if (RunFileDialogs.TryImportShortcuts(_shortcuts, Window.GetWindow(this), replace, out var message)
            && !string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string message) => _statusText.Text = message;
}
