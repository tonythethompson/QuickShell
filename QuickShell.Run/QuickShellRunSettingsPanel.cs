using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Services;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickShell.Run;

internal sealed class QuickShellRunSettingsPanel : UserControl
{
    private readonly QuickShellSettingsReader _settings;
    private readonly IShortcutRepository _shortcuts;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _commandSuggestions;
    private readonly IWorkspaceGitOperations _gitOperations;
    private readonly IWorktreeBranchTargetStore _targetStore;
    private readonly ITerminalCatalog _catalog;

    public QuickShellRunSettingsPanel(
        QuickShellSettingsReader settings,
        IShortcutRepository shortcuts,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IWorkspaceGitOperations gitOperations,
        IWorktreeBranchTargetStore targetStore,
        ITerminalCatalog catalog,
        Action<string, string> onDefaultsSaved)
    {
        _ = onDefaultsSaved;
        _settings = settings;
        _shortcuts = shortcuts;
        _projectAnalysis = projectAnalysis;
        _commandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
        _gitOperations = gitOperations ?? throw new ArgumentNullException(nameof(gitOperations));
        _targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        var root = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };

        var card = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var cardBody = new StackPanel();
        cardBody.Children.Add(new TextBlock
        {
            Text = "Quick Shell app settings",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        cardBody.Children.Add(new TextBlock
        {
            Text = "Terminal defaults, git launch, recents, and shortcut import/export.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var openSettings = new Button
        {
            Content = "Open Quick Shell settings…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 36,
            FontWeight = FontWeights.SemiBold,
        };
        openSettings.Click += (_, _) => QuickShellRunSettingsDialog.Show(_settings, _shortcuts, _projectAnalysis, _catalog);
        cardBody.Children.Add(openSettings);
        card.Child = cardBody;
        root.Children.Add(card);

        root.Children.Add(new TextBlock
        {
            Text = "Quick actions",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 8),
        });
        root.Children.Add(CreateButton(
            "Create shortcut",
            () => ShortcutEditor.TryShowDialog(null, _shortcuts, _projectAnalysis, _commandSuggestions, _gitOperations, _targetStore, _catalog, out _)));

        Content = root;
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Reserved for future settings-panel wiring.")]
    public void Reload()
    {
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Reserved for future settings-panel wiring.")]
    public void UpdateSettings(Microsoft.PowerToys.Settings.UI.Library.PowerLauncherPluginSettings settings)
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
}

