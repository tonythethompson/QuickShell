using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Services;

using System.Windows;

using System.Windows.Controls;

namespace QuickShell.Run;

internal sealed class QuickShellRunSettingsWindow : Window
{
    private readonly QuickShellSettingsReader _settings;

    private readonly IShortcutRepository _shortcuts;

    private readonly IProjectAnalysisService _projectAnalysis;

    private readonly ITerminalCatalog _catalog;

    private readonly ComboBox _terminalAppBox;

    private readonly ComboBox _defaultProfileBox;

    private readonly CheckBox _blockDirtyBranchBox;

    private readonly CheckBox _showRecentsBox;

    private readonly CheckBox _singleWindowTabsBox;

    private readonly TextBlock _statusText;

    private SettingsSnapshot _baseline;

    public QuickShellRunSettingsWindow(

        QuickShellSettingsReader settings,

        IShortcutRepository shortcuts,

        IProjectAnalysisService projectAnalysis,

        ITerminalCatalog catalog)
    {
        _settings = settings;

        _shortcuts = shortcuts;

        _projectAnalysis = projectAnalysis;

        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        Title = "Quick Shell settings";

        Width = 520;

        MinHeight = 420;

        SizeToContent = SizeToContent.Height;

        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(Heading("Terminal defaults"));

        root.Children.Add(Label("Terminal application"));

        _terminalAppBox = Combo(RunTerminalChoices.GetTerminalApplicationChoices(_catalog));

        _terminalAppBox.SelectionChanged += (_, _) => ReloadProfileChoices();

        root.Children.Add(_terminalAppBox);

        root.Children.Add(Label("Default profile"));

        _defaultProfileBox = new ComboBox
        {
            DisplayMemberPath = "Label",

            SelectedValuePath = "Id",

            Margin = new Thickness(0, 0, 0, 8),
        };

        root.Children.Add(_defaultProfileBox);

        root.Children.Add(Heading("Git launch"));

        _blockDirtyBranchBox = new CheckBox
        {
            Content = "Block launch when dirty and branch would change",

            Margin = new Thickness(0, 0, 0, 8),
        };

        root.Children.Add(_blockDirtyBranchBox);

        root.Children.Add(Help(

            "When a worktree target branch differs from HEAD, block launch if the working tree has uncommitted changes."));

        root.Children.Add(Heading("Home list"));

        _showRecentsBox = new CheckBox
        {
            Content = "Show recent workspaces",

            Margin = new Thickness(0, 0, 0, 8),
        };

        root.Children.Add(_showRecentsBox);

        root.Children.Add(Heading("Multiple commands"));

        _singleWindowTabsBox = new CheckBox
        {
            Content = "Open multiple commands in one Windows Terminal window",

            Margin = new Thickness(0, 0, 0, 8),
        };

        root.Children.Add(_singleWindowTabsBox);

        root.Children.Add(Help(

            "When supported, extra commands open as tabs in the same window. Mixed elevation or Console Host still opens separate windows."));

        root.Children.Add(Heading("Shortcuts"));

        root.Children.Add(TooltipButton(

            "Export shortcuts…",

            "Save all shortcuts to a JSON file you can back up or share.",

            ExportShortcuts));

        root.Children.Add(TooltipButton(

            "Import and merge…",

            "Add shortcuts from a JSON file without removing existing ones.",

            () => ImportShortcuts(replace: false)));

        root.Children.Add(TooltipButton(

            "Import and replace all…",

            "Replace every shortcut with the contents of a JSON file.",

            () => ImportShortcuts(replace: true)));

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        root.Children.Add(TooltipButton(

            "Open shortcuts.json",

            "Open the shortcuts file in your default editor.",

            () => RunFileDialogs.OpenPathInEditor(_shortcuts.ConfigPath)));

        root.Children.Add(TooltipButton(

            "Open Quick Shell data folder",

            "Open the folder that stores shortcuts and settings.",

            () => RunFileDialogs.OpenFolder(_shortcuts.ConfigDirectory)));

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,

            Margin = new Thickness(0, 12, 0, 12),

            Foreground = System.Windows.Media.Brushes.Gray,
        };

        root.Children.Add(_statusText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) =>
        {
            RestoreBaseline();
            Close();
        };
        var save = new Button { Content = "Save", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) =>
        {
            Save();
            SetStatus("Settings saved.");
        };
        var done = new Button { Content = "Done", MinWidth = 88, IsDefault = true };
        done.Click += (_, _) =>
        {
            if (HasUnsavedChanges())
            {
                Save();
            }

            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        buttons.Children.Add(done);
        root.Children.Add(buttons);

        Content = root;

        LoadCurrentValues();

        _baseline = CaptureSnapshot();
    }

    private void LoadCurrentValues()
    {
        _terminalAppBox.SelectedValue = _settings.TerminalApplicationId;

        ReloadProfileChoices();

        _defaultProfileBox.SelectedValue = _settings.DefaultProfileId;

        _blockDirtyBranchBox.IsChecked = _settings.ReadBlockDirtyBranchSwitch();

        _showRecentsBox.IsChecked = QuickShellRecentSettings.IsEnabled(_settings.ReadRecentWorkspaceCount());

        _singleWindowTabsBox.IsChecked = !_settings.ReadSeparateWindowsForMultiLaunch();
    }

    private void RestoreBaseline()
    {
        _settings.SaveTerminalDefaults(_baseline.TerminalApp, _baseline.DefaultProfile);

        _settings.SaveBlockDirtyBranchSwitch(_baseline.BlockDirtyBranch);

        _settings.SaveRecentWorkspaceCount(_baseline.RecentCount);

        _settings.SaveMultiLaunchPresentation(_baseline.SingleWindowTabs);
    }

    private SettingsSnapshot CaptureSnapshot() =>

        new(

            _terminalAppBox.SelectedValue as string ?? _settings.TerminalApplicationId,

            _defaultProfileBox.SelectedValue as string ?? _settings.DefaultProfileId,

            _blockDirtyBranchBox.IsChecked == true,

            QuickShellRecentSettings.FromEnabled(_showRecentsBox.IsChecked == true),

            _singleWindowTabsBox.IsChecked == true);

    private void ReloadProfileChoices()
    {
        var app = _terminalAppBox.SelectedValue as string ?? _settings.TerminalApplicationId;

        var selected = _defaultProfileBox.SelectedValue as string ?? _settings.DefaultProfileId;

        _defaultProfileBox.Items.Clear();

        foreach (var choice in RunTerminalChoices.GetDefaultProfileChoices(_catalog, app))
        {
            _defaultProfileBox.Items.Add(new { choice.Id, choice.Label });
        }

        _defaultProfileBox.SelectedValue = RunTerminalChoices.GetDefaultProfileChoices(_catalog, app)

            .Any(choice => choice.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))

            ? selected

            : TerminalHostIds.DefaultProfile;
    }

    private bool HasUnsavedChanges()
    {
        var current = CaptureSnapshot();
        return !current.TerminalApp.Equals(_baseline.TerminalApp, StringComparison.OrdinalIgnoreCase)
            || !current.DefaultProfile.Equals(_baseline.DefaultProfile, StringComparison.OrdinalIgnoreCase)
            || current.BlockDirtyBranch != _baseline.BlockDirtyBranch
            || current.RecentCount != _baseline.RecentCount
            || current.SingleWindowTabs != _baseline.SingleWindowTabs;
    }

    private bool Save()
    {
        var app = _terminalAppBox.SelectedValue as string ?? TerminalHostIds.LetWindowsChoose;

        var profile = _defaultProfileBox.SelectedValue as string ?? TerminalHostIds.DefaultProfile;

        _settings.SaveTerminalDefaults(app, profile);

        _settings.SaveBlockDirtyBranchSwitch(_blockDirtyBranchBox.IsChecked == true);

        _settings.SaveRecentWorkspaceCount(

            QuickShellRecentSettings.FromEnabled(_showRecentsBox.IsChecked == true));

        _settings.SaveMultiLaunchPresentation(_singleWindowTabsBox.IsChecked == true);

        _baseline = CaptureSnapshot();

        return true;
    }

    private void ExportShortcuts()
    {
        if (RunFileDialogs.TryExportShortcuts(_shortcuts, this, out var message))
        {
            SetStatus(message);
        }
    }

    private void ImportShortcuts(bool replace)
    {
        if (RunFileDialogs.TryImportShortcuts(_shortcuts, this, replace, out var message)

            && !string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string message) => _statusText.Text = message;

    private static TextBlock Heading(string text) => new()
    {
        Text = text,

        FontWeight = FontWeights.SemiBold,

        Margin = new Thickness(0, 12, 0, 8),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,

        Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock Help(string text) => new()
    {
        Text = text,

        TextWrapping = TextWrapping.Wrap,

        Margin = new Thickness(0, 0, 0, 8),

        Foreground = System.Windows.Media.Brushes.Gray,

        FontSize = 12,
    };

    private static ComboBox Combo(IReadOnlyList<(string Id, string Label)> choices)
    {
        var box = new ComboBox
        {
            DisplayMemberPath = "Label",

            SelectedValuePath = "Id",

            Margin = new Thickness(0, 0, 0, 8),
        };

        foreach (var choice in choices)
        {
            box.Items.Add(new { choice.Id, choice.Label });
        }

        return box;
    }

    private static Button TooltipButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,

            HorizontalAlignment = HorizontalAlignment.Stretch,

            Margin = new Thickness(0, 0, 0, 6),

            MinWidth = 220,

            ToolTip = tooltip,
        };

        button.Click += (_, _) => action();

        return button;
    }

    private readonly record struct SettingsSnapshot(

        string TerminalApp,

        string DefaultProfile,

        bool BlockDirtyBranch,

        int RecentCount,

        bool SingleWindowTabs);
}

internal static class QuickShellRunSettingsDialog
{
    public static void Show(
        QuickShellSettingsReader settings,
        IShortcutRepository shortcuts,
        IProjectAnalysisService projectAnalysis,
        ITerminalCatalog catalog)
    {
        void ShowWindow()
        {
            var window = new QuickShellRunSettingsWindow(settings, shortcuts, projectAnalysis, catalog);

            window.ShowDialog();
        }

        var app = Application.Current;

        if (app?.Dispatcher.CheckAccess() == true)
        {
            ShowWindow();
        }
        else
        {
            app?.Dispatcher.Invoke(ShowWindow);
        }
    }
}


