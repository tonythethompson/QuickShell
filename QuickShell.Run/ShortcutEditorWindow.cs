using QuickShell.Models;
using QuickShell.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace QuickShell.Run;

internal sealed class ShortcutEditorWindow : Window
{
    private readonly TerminalShortcut? _existing;
    private readonly ShortcutRepository _shortcuts;
    private readonly TextBox _nameBox;
    private readonly TextBox _abbreviationBox;
    private readonly TextBox _directoryBox;
    private readonly TextBox _commandBox;
    private readonly ComboBox _terminalBox;
    private readonly CheckBox _adminBox;

    public string ResultMessage { get; private set; } = string.Empty;

    public ShortcutEditorWindow(TerminalShortcut? existing, ShortcutRepository shortcuts)
    {
        _existing = existing;
        _shortcuts = shortcuts;

        var primaryLaunch = existing is not null
            ? ShortcutFormSave.GetPrimaryLaunchForRunEditor(existing)
            : null;

        Title = existing is null ? "Create Quick Shell shortcut" : $"Edit {existing.Name}";
        Width = 560;
        MinHeight = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        _nameBox = AddField(root, "Name", existing?.Name ?? string.Empty);
        _abbreviationBox = AddField(root, "Home keyword", existing?.Abbreviation ?? string.Empty);
        _directoryBox = AddField(root, "Folder", existing?.Directory ?? string.Empty);

        var browseButton = new Button
        {
            Content = "Browse folder…",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        };
        browseButton.Click += (_, _) =>
        {
            var picked = FolderPickerService.PickFolder(_directoryBox.Text);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            _directoryBox.Text = picked;
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                _nameBox.Text = Path.GetFileName(picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        };
        root.Children.Add(browseButton);

        _commandBox = AddField(root, "Command (optional)", primaryLaunch?.Command ?? existing?.Command ?? string.Empty);

        root.Children.Add(new TextBlock
        {
            Text = "Terminal profile",
            Margin = new Thickness(0, 8, 0, 4),
        });

        _terminalBox = new ComboBox
        {
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 0, 8),
        };
        foreach (var choice in RunTerminalChoices.GetLaunchTargetChoices())
        {
            _terminalBox.Items.Add(new { choice.Id, choice.Label });
        }

        _terminalBox.SelectedValue = primaryLaunch is not null
            ? ShortcutFormSave.EncodeLaunchTargetForEntry(primaryLaunch)
            : TerminalCatalog.EncodeLaunchTargetId(existing ?? new TerminalShortcut());
        root.Children.Add(_terminalBox);

        _adminBox = new CheckBox
        {
            Content = "Launch elevated",
            IsChecked = primaryLaunch?.RunAsAdmin ?? existing?.RunAsAdmin ?? false,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(_adminBox);

        var preserveNote = BuildPreserveNote(existing);
        if (!string.IsNullOrWhiteSpace(preserveNote))
        {
            root.Children.Add(new TextBlock
            {
                Text = preserveNote,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 12,
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var save = new Button { Content = "Save", MinWidth = 88, IsDefault = true };
        save.Click += (_, _) => SaveShortcut();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
    }

    private static string? BuildPreserveNote(TerminalShortcut? existing)
    {
        if (existing is null)
        {
            return null;
        }

        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(existing);
        var enabledCount = ShortcutLaunchNormalization.GetEnabledLaunches(existing).Count;
        var parts = new List<string>();

        if (enabledCount > 1)
        {
            parts.Add(
                $"Command, terminal, and elevation apply to the primary launch only. {enabledCount - 1} other launch{(enabledCount == 2 ? string.Empty : "es")} are preserved.");
        }

        if (existing.OpenCompanionAppOnLaunch
            || !string.IsNullOrWhiteSpace(existing.CompanionAppPath)
            || !string.IsNullOrWhiteSpace(existing.DevServerUrl)
            || !string.IsNullOrWhiteSpace(existing.RepoUrl))
        {
            parts.Add("Companion app and link settings are preserved. Edit those in Command Palette.");
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static TextBox AddField(StackPanel root, string label, string value)
    {
        root.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 8, 0, 4),
        });

        var box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 0, 0, 4),
        };
        root.Children.Add(box);
        return box;
    }

    private void SaveShortcut()
    {
        var launchTarget = _terminalBox.SelectedValue as string ?? "default";
        var result = ShortcutFormSave.TrySaveRunEditor(
            _existing,
            _existing?.Name,
            _nameBox.Text,
            _abbreviationBox.Text,
            _directoryBox.Text,
            _commandBox.Text,
            launchTarget,
            _adminBox.IsChecked == true,
            _shortcuts,
            onSaved: null);

        ResultMessage = result.Message;
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "Quick Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}

internal static class ShortcutEditor
{
    public static bool TryShowDialog(TerminalShortcut? existing, ShortcutRepository shortcuts, out string message)
    {
        message = string.Empty;
        var saved = false;
        var resultMessage = string.Empty;

        void Show()
        {
            var window = new ShortcutEditorWindow(existing, shortcuts);
            if (window.ShowDialog() == true)
            {
                saved = true;
                resultMessage = window.ResultMessage;
            }
        }

        var app = Application.Current;
        if (app?.Dispatcher.CheckAccess() == true)
        {
            Show();
        }
        else
        {
            app?.Dispatcher.Invoke(Show);
        }

        if (saved)
        {
            message = resultMessage;
        }

        return saved;
    }
}
