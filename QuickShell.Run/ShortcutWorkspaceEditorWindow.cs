using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Core.Services;
using QuickShell.Models;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;
using System.IO;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace QuickShell.Run;

internal sealed class ShortcutWorkspaceEditorWindow : Window, IDisposable
{
    private readonly TerminalShortcut? _existing;
    private readonly IShortcutRepository _shortcuts;
    private readonly IWorkspaceEditor _editor;
    private readonly TerminalShortcut _working;
    private readonly TextBox _nameBox;
    private readonly TextBox _abbreviationBox;
    private readonly TextBox _directoryBox;
    private readonly ComboBox _targetBranchBox;
    private readonly StackPanel _launchesPanel;
    private readonly TextBlock _emptyLaunchesText;
    private readonly TextBox _devServerUrlBox;
    private readonly CheckBox _openDevServerBox;
    private readonly TextBox _repoUrlBox;
    private readonly StackPanel _companionRowsHost = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly List<CompanionRowUi> _companionRows = [];
    private readonly List<LaunchRow> _launchRows = [];
    private readonly RunLaunchSuggestionPanel _suggestionPanel = new();
    private readonly FormEditHistory<List<RunLaunchRowSnapshot>> _editHistory =
        new(snapshot => snapshot.Select(entry => entry with { }).ToList());
    private readonly RunDirectorySuggestionLoader _suggestionLoader;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _commandSuggestions;
    private readonly IWorkspaceGitOperations _gitOperations;
    private readonly IWorktreeBranchTargetStore _targetStore;
    private readonly ITerminalCatalog _catalog;
    private readonly LaunchEditorText _launchText = LaunchEditorText.EnglishDefaults;
    private int _activeSuggestionGeneration;

    public string ResultMessage { get; private set; } = string.Empty;

    public ShortcutWorkspaceEditorWindow(
        TerminalShortcut? existing,
        IShortcutRepository shortcuts,
        IWorkspaceEditorFactory editorFactory,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IWorkspaceGitOperations gitOperations,
        IWorktreeBranchTargetStore targetStore,
        ITerminalCatalog catalog)
    {
        _existing = existing;
        _shortcuts = shortcuts;
        _projectAnalysis = projectAnalysis ?? throw new ArgumentNullException(nameof(projectAnalysis));
        _commandSuggestions = commandSuggestions ?? throw new ArgumentNullException(nameof(commandSuggestions));
        _gitOperations = gitOperations ?? throw new ArgumentNullException(nameof(gitOperations));
        _targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArgumentNullException.ThrowIfNull(editorFactory);
        _editor = editorFactory.Create();
        _editor.ResetForOpen(existing, createSeed: null);
        var state = _editor.GetState();
        _working = existing is null ? new TerminalShortcut() : CloneShortcut(existing);
        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(_working);
        _working.Name = state.Name;
        _working.Abbreviation = string.IsNullOrWhiteSpace(state.Abbreviation) ? null : state.Abbreviation;
        _working.Directory = state.Directory;
        _working.DevServerUrl = string.IsNullOrWhiteSpace(state.DevServerUrl) ? null : state.DevServerUrl;
        _working.OpenDevServerOnLaunch = state.OpenDevServerOnLaunch;
        _working.RepoUrl = string.IsNullOrWhiteSpace(state.RepoUrl) ? null : state.RepoUrl;

        Title = existing is null ? "Create Quick Shell workspace" : $"Edit {_working.Name}";
        Width = 680;
        // Tall enough for folder/name/links/companion + pills + a few launch rows without scrolling.
        Height = 860;
        MinHeight = 640;
        MinWidth = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(16) };
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        body.Children.Add(RunWpfUiHelpers.FieldLabel("Folder", WorkspaceFormTooltips.Directory));
        _directoryBox = new TextBox { Text = _working.Directory, Margin = new Thickness(0, 0, 0, 4) };
        body.Children.Add(_directoryBox);
        body.Children.Add(CreateBrowseFolderButton());

        body.Children.Add(RunWpfUiHelpers.FieldLabel(
            "Target branch (optional)",
            "Git branch to switch to when this workspace launches. Pick from local branches or type a branch name."));
        _targetBranchBox = CreateEditableCombo();
        body.Children.Add(_targetBranchBox);
        ReloadBranchChoices(_working.Directory, _targetStore.GetTargetForDirectory(_working.Directory, _gitOperations));

        body.Children.Add(RunWpfUiHelpers.FieldLabel("Name", WorkspaceFormTooltips.Name));
        _nameBox = new TextBox { Text = _working.Name, Margin = new Thickness(0, 0, 0, 8) };
        body.Children.Add(_nameBox);

        body.Children.Add(RunWpfUiHelpers.FieldLabel(
            "Home keyword (optional)",
            WorkspaceFormTooltips.HomeKeyword));
        _abbreviationBox = new TextBox
        {
            Text = _working.Abbreviation ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = WorkspaceFormTooltips.HomeKeywordExample,
        };
        body.Children.Add(_abbreviationBox);

        var linkRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        linkRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        linkRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var repoLabel = RunWpfUiHelpers.FieldLabel("Repository URL", WorkspaceFormTooltips.RepoUrl);
        Grid.SetRow(repoLabel, 0);
        Grid.SetColumn(repoLabel, 0);
        linkRow.Children.Add(repoLabel);
        var devServerLabel = RunWpfUiHelpers.FieldLabel("Dev server URL", WorkspaceFormTooltips.DevServerUrl);
        Grid.SetRow(devServerLabel, 0);
        Grid.SetColumn(devServerLabel, 2);
        linkRow.Children.Add(devServerLabel);
        _repoUrlBox = new TextBox
        {
            Text = _working.RepoUrl ?? string.Empty,
            ToolTip = WorkspaceFormTooltips.RepoUrlExample,
            // Match single-line height; the sibling column has TextBox + checkbox and would stretch this cell.
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetRow(_repoUrlBox, 1);
        Grid.SetColumn(_repoUrlBox, 0);
        linkRow.Children.Add(_repoUrlBox);
        var devServerColumn = new StackPanel();
        _devServerUrlBox = new TextBox
        {
            Text = _working.DevServerUrl ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = WorkspaceFormTooltips.DevServerUrlExample,
        };
        devServerColumn.Children.Add(_devServerUrlBox);
        _openDevServerBox = new CheckBox
        {
            Content = "Open dev server on launch",
            IsChecked = _working.OpenDevServerOnLaunch,
            ToolTip = WorkspaceFormTooltips.DevServerOnLaunch,
        };
        devServerColumn.Children.Add(_openDevServerBox);
        Grid.SetRow(devServerColumn, 1);
        Grid.SetColumn(devServerColumn, 2);
        linkRow.Children.Add(devServerColumn);
        body.Children.Add(linkRow);

        body.Children.Add(_companionRowsHost);
        RebuildCompanionRows(state.Companions.Count > 0 ? state.Companions : CompanionAppFormEditor.FromShortcut(_working));

        body.Children.Add(RunWpfUiHelpers.FieldLabel(
            _launchText.CommandsSectionTitle,
            _launchText.CommandsSectionTooltip));
        _suggestionPanel.PillClicked += HandleSuggestionPillClicked;
        body.Children.Add(_suggestionPanel.Root);

        _emptyLaunchesText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 4),
            Visibility = Visibility.Collapsed,
        };
        body.Children.Add(_emptyLaunchesText);

        _launchesPanel = new StackPanel();
        body.Children.Add(_launchesPanel);

        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var addCommand = new Button
        {
            Content = _launchText.AddCommand,
            MinWidth = 110,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = FormActionGlyphs.AddCommandTooltip,
        };
        addCommand.Click += (_, _) => AddCommandLaunchRow();
        var addTerminal = new Button
        {
            Content = _launchText.AddOpenInTerminal,
            MinWidth = 110,
        };
        addTerminal.Click += (_, _) => AddOpenInTerminalLaunchRow();
        addRow.Children.Add(addCommand);
        addRow.Children.Add(addTerminal);
        body.Children.Add(addRow);
        LoadLaunchRows(state.Commands);

        // Dock bottom chrome first so the ScrollViewer fills remaining space (LastChildFill).
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "Cancel", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var save = new Button { Content = "Save", MinWidth = 88, IsDefault = true };
        save.Click += (_, _) => SaveWorkspace();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        root.Children.Add(scroller);

        _suggestionLoader = new RunDirectorySuggestionLoader(Dispatcher);
        Closed += (_, _) => Dispose();
        _directoryBox.TextChanged += (_, _) => RefreshSuggestionPanel();
        PreviewKeyDown += OnPreviewKeyDown;
        Content = root;
        RefreshSuggestionPanel();
    }

    private static ComboBox CreateEditableCombo() =>
        new()
        {
            IsEditable = true,
            Margin = new Thickness(0, 0, 0, 8),
        };
    public void Dispose()
    {
        _suggestionLoader.Dispose();
        _editor.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ReloadBranchChoices(string directory, string? selectedBranch)
    {
        _targetBranchBox.Items.Clear();
        foreach (var branch in _gitOperations.ListLocalBranches(directory))
        {
            _targetBranchBox.Items.Add(branch);
        }

        _targetBranchBox.Text = selectedBranch ?? string.Empty;
    }

    private Button CreateBrowseFolderButton()
    {
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

            ApplyDirectorySelection(picked);
        };
        return browseButton;
    }

    private void ApplyDirectorySelection(string directory)
    {
        _ = _editor.SelectDirectory(directory);
        var state = _editor.GetState();
        _directoryBox.Text = state.Directory;
        _nameBox.Text = state.Name;
        _repoUrlBox.Text = state.RepoUrl;
        _devServerUrlBox.Text = state.DevServerUrl;
        ReloadBranchChoices(state.Directory, _targetStore.GetTargetForDirectory(state.Directory, _gitOperations));
        RefreshSuggestionPanel();
    }

    private void RebuildCompanionRows(IReadOnlyList<CompanionAppFormRow> rows)
    {
        _companionRowsHost.Children.Clear();
        _companionRows.Clear();
        var source = rows.ToList();
        CompanionAppFormEditor.EnsureAtLeastOne(source);
        for (var i = 0; i < source.Count; i++)
        {
            AddCompanionRowUi(source[i], i, source.Count);
        }
    }
    private void AddCompanionRowUi(CompanionAppFormRow model, int index, int totalCount)
    {
        var row = new CompanionRowUi(this, model, index, totalCount);
        _companionRows.Add(row);
        _companionRowsHost.Children.Add(row.Root);
    }
    private void RefreshCompanionRowChrome()
    {
        var total = _companionRows.Count;
        for (var i = 0; i < total; i++)
        {
            _companionRows[i].RefreshChrome(i, total);
        }
    }
    private void OnAddCompanionRow()
    {
        if (!CompanionAppFormEditor.CanAdd(CaptureCompanionFormRows()))
        {
            return;
        }
        AddCompanionRowUi(CompanionAppFormRow.Empty(), _companionRows.Count, _companionRows.Count + 1);
        RefreshCompanionRowChrome();
    }
    private void OnRemoveCompanionRow(CompanionRowUi row)
    {
        if (_companionRows.Count <= 1)
        {
            return;
        }
        _companionRows.Remove(row);
        _companionRowsHost.Children.Remove(row.Root);
        RefreshCompanionRowChrome();
    }
    private List<CompanionAppFormRow> CaptureCompanionFormRows()
    {
        var rows = _companionRows.Select(row => row.ToFormRow()).ToList();
        CompanionAppFormEditor.EnsureAtLeastOne(rows);
        return rows;
    }

    private void LoadLaunchRows(IReadOnlyList<LaunchRowDraft>? commands = null)
    {
        _launchesPanel.Children.Clear();
        _launchRows.Clear();
        var order = 0;
        // Match CmdPal: only real launches (no placeholder padding). New workspaces start empty.
        if (commands is { Count: > 0 })
        {
            foreach (var command in commands)
            {
                if (command.IsEditorPlaceholder)
                {
                    continue;
                }

                AddLaunchRowFromDraft(command, order++);
            }
        }

        RefreshEmptyLaunchesState();
    }

    private void RefreshEmptyLaunchesState()
    {
        if (_launchRows.Count == 0)
        {
            _emptyLaunchesText.Text = $"{_launchText.EmptyTitle}\n{_launchText.EmptyGuidance}";
            _emptyLaunchesText.Visibility = Visibility.Visible;
        }
        else
        {
            _emptyLaunchesText.Visibility = Visibility.Collapsed;
        }
    }

    private void AddLaunchRowFromDraft(LaunchRowDraft draft, int order)
    {
        var entry = new WorkspaceEntry
        {
            Id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id,
            Label = string.IsNullOrWhiteSpace(draft.Label) ? $"Launch {order + 1}" : draft.Label,
            Command = draft.Command,
            RunAsAdmin = draft.RunAsAdmin,
            IsEnabled = draft.IsEnabled,
            Order = order,
            TaskType = TaskTypeCatalog.Normalize(draft.TaskType),
        };
        ApplyLaunchTarget(entry, string.IsNullOrWhiteSpace(draft.LaunchTarget) ? "default" : draft.LaunchTarget);
        AddLaunchRow(entry, order, draft.Kind, insertAt: null);
    }

    private void AddCommandLaunchRow()
    {
        if (_launchRows.Count >= ShortcutLaunchNormalization.MaxLaunchCount)
        {
            MessageBox.Show(
                this,
                $"At most {ShortcutLaunchNormalization.MaxLaunchCount} launch entries are supported.",
                "Quick Shell",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        PushFormEditSnapshot();
        var launchTarget = _launchRows.Count == 0
            ? "default"
            : TerminalCatalog.SameAsPreviousLaunchTargetId;
        var label = CreateUniqueLaunchLabel("Command");
        AddLaunchRow(
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label,
                Command = string.Empty,
                IsEnabled = true,
                Order = _launchRows.Count,
            },
            _launchRows.Count,
            LaunchRowKind.Command,
            insertAt: null,
            launchTargetOverride: launchTarget);
        RefreshEmptyLaunchesState();
        RefreshSuggestionPanel();
    }

    private void AddOpenInTerminalLaunchRow()
    {
        if (_launchRows.Any(row => row.Kind == LaunchRowKind.OpenInTerminal))
        {
            MessageBox.Show(
                this,
                $"Only one '{_launchText.OpenInTerminal}' launch is allowed per workspace.",
                "Quick Shell",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_launchRows.Count >= ShortcutLaunchNormalization.MaxLaunchCount)
        {
            MessageBox.Show(
                this,
                $"At most {ShortcutLaunchNormalization.MaxLaunchCount} launch entries are supported.",
                "Quick Shell",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        PushFormEditSnapshot();
        // Terminal-only launches default to first: shell then tasks is the common tab order.
        AddLaunchRow(
            new WorkspaceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = CreateUniqueLaunchLabel(_launchText.OpenInTerminal),
                Command = string.Empty,
                IsEnabled = true,
                Order = 0,
            },
            order: 0,
            LaunchRowKind.OpenInTerminal,
            insertAt: 0,
            launchTargetOverride: "default");
        RefreshEmptyLaunchesState();
        RefreshSuggestionPanel();
    }

    private void AddLaunchRow(
        WorkspaceEntry? launch,
        int order,
        LaunchRowKind kind,
        int? insertAt,
        string? launchTargetOverride = null)
    {
        var row = new LaunchRow(
            this,
            launch,
            entryId: null,
            kind,
            launch?.Label ?? $"Launch {order + 1}",
            launch?.Command ?? string.Empty,
            launchTargetOverride
                ?? (launch is null ? "default" : ShortcutFormSave.EncodeLaunchTargetForEntry(launch)),
            launch?.RunAsAdmin ?? false,
            launch?.IsEnabled ?? true,
            TaskTypeCatalog.Normalize(launch?.TaskType),
            order);
        if (insertAt is int index)
        {
            _launchRows.Insert(index, row);
            _launchesPanel.Children.Insert(index, row.Root);
        }
        else
        {
            _launchRows.Add(row);
            _launchesPanel.Children.Add(row.Root);
        }
    }

    private void RemoveLaunchRow(LaunchRow row)
    {
        PushFormEditSnapshot();
        _launchRows.Remove(row);
        _launchesPanel.Children.Remove(row.Root);
        RefreshEmptyLaunchesState();
        RefreshSuggestionPanel();
    }

    private string CreateUniqueLaunchLabel(string labelBase)
    {
        var labels = _launchRows
            .Select(row => row.LabelText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!labels.Contains(labelBase))
        {
            return labelBase;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{labelBase} {suffix}";
            if (!labels.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void RefreshSuggestionPanel()
    {
        var directory = _directoryBox.Text.Trim();
        var usedCommands = _launchRows.Select(row => row.CommandText).ToList();
        _suggestionLoader.Schedule(
            _projectAnalysis,
            _commandSuggestions,
            directory,
            usedCommands,
            generation =>
            {
                _activeSuggestionGeneration = generation;
                _suggestionPanel.SetLoading(true);
            },
            (pills, generation) =>
            {
                if (generation != _activeSuggestionGeneration)
                {
                    return Task.CompletedTask;
                }

                _suggestionPanel.SetPills(pills);
                return Task.CompletedTask;
            });
    }

    private void HandleSuggestionPillClicked(CommandSuggestionPill pill)
    {
        _editHistory.PushBeforeChange(CaptureSnapshot());
        var target = _launchRows.FirstOrDefault(row =>
            row.Kind == LaunchRowKind.Command
            && string.IsNullOrWhiteSpace(row.CommandText));
        if (target is null)
        {
            if (_launchRows.Count >= ShortcutLaunchNormalization.MaxLaunchCount)
            {
                MessageBox.Show(
                    this,
                    $"At most {ShortcutLaunchNormalization.MaxLaunchCount} launch entries are supported.",
                    "Quick Shell",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var launchTarget = _launchRows.Count == 0
                ? "default"
                : TerminalCatalog.SameAsPreviousLaunchTargetId;
            AddLaunchRow(
                new WorkspaceEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = CreateUniqueLaunchLabel("Command"),
                    Command = string.Empty,
                    IsEnabled = true,
                    Order = _launchRows.Count,
                },
                _launchRows.Count,
                LaunchRowKind.Command,
                insertAt: null,
                launchTargetOverride: launchTarget);
            target = _launchRows[^1];
            RefreshEmptyLaunchesState();
        }

        target.ApplyPill(pill);
        RefreshSuggestionPanel();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (TryFormUndo())
            {
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (TryFormRedo())
            {
                e.Handled = true;
            }
        }
    }

    private bool TryFormUndo()
    {
        if (!_editHistory.TryUndo(CaptureSnapshot(), out var restored))
        {
            return false;
        }

        RestoreSnapshot(restored);
        RefreshSuggestionPanel();
        return true;
    }

    private bool TryFormRedo()
    {
        if (!_editHistory.TryRedo(CaptureSnapshot(), out var restored))
        {
            return false;
        }

        RestoreSnapshot(restored);
        RefreshSuggestionPanel();
        return true;
    }

    private List<RunLaunchRowSnapshot> CaptureSnapshot() =>
        _launchRows.Select(row => row.CaptureSnapshot()).ToList();

    internal void PushFormEditSnapshot() => _editHistory.PushBeforeChange(CaptureSnapshot());

    private void RestoreSnapshot(List<RunLaunchRowSnapshot> snapshots)
    {
        _launchesPanel.Children.Clear();
        _launchRows.Clear();
        var order = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.IsEditorPlaceholder)
            {
                continue;
            }

            AddLaunchRow(
                snapshot.Id,
                snapshot.Kind,
                snapshot.Label,
                snapshot.Command,
                snapshot.LaunchTarget,
                snapshot.RunAsAdmin,
                snapshot.IsEnabled,
                snapshot.TaskType,
                order++);
        }

        RefreshEmptyLaunchesState();
    }

    private void AddLaunchRow(
        string id,
        LaunchRowKind kind,
        string label,
        string command,
        string launchTarget,
        bool runAsAdmin,
        bool isEnabled,
        string taskType,
        int order)
    {
        var row = new LaunchRow(
            this,
            null,
            id,
            kind,
            label,
            command,
            launchTarget,
            runAsAdmin,
            isEnabled,
            taskType,
            order);
        _launchRows.Add(row);
        _launchesPanel.Children.Add(row.Root);
    }

    private void SaveWorkspace()
    {
        var launchDrafts = _launchRows
            .Select(row => row.ToLaunchRowDraft())
            .ToList();
        var applied = _editor.TryApplyHostFields(new WorkspaceHostFieldUpdate
        {
            Name = _nameBox.Text.Trim(),
            Abbreviation = _abbreviationBox.Text.Trim(),
            Directory = _directoryBox.Text.Trim(),
            DevServerUrl = _devServerUrlBox.Text.Trim(),
            OpenDevServerOnLaunch = _openDevServerBox.IsChecked == true,
            RepoUrl = _repoUrlBox.Text.Trim(),
            Commands = launchDrafts,
            Companions = CaptureCompanionFormRows(),
        });
        if (!applied)
        {
            MessageBox.Show(this, "Could not apply form values.", "Quick Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetBranch = _targetBranchBox.Text?.Trim();
        var directory = _directoryBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(targetBranch)
            && _gitOperations.TryResolveWorktreeKey(directory, out _))
        {
            var branches = _gitOperations.ListLocalBranches(directory);
            if (branches.Count > 0
                && !branches.Contains(targetBranch, StringComparer.OrdinalIgnoreCase))
            {
                var confirm = MessageBox.Show(
                    this,
                    $"Branch '{targetBranch}' was not found locally. Save it anyway?",
                    "Quick Shell",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }
        }

        var result = _editor.Save();
        if (result.Kind != WorkspaceEditResultKind.Saved)
        {
            var message = result.Message ?? _editor.GetState().SaveError ?? "Could not save workspace.";
            MessageBox.Show(this, message, "Quick Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_targetStore.TrySetTargetForDirectory(
                directory,
                string.IsNullOrWhiteSpace(targetBranch) ? null : targetBranch,
                _gitOperations,
                out var branchError))
        {
            if (!string.IsNullOrWhiteSpace(targetBranch))
            {
                MessageBox.Show(
                    this,
                    (branchError ?? "Could not save target branch.") + " Workspace was saved.",
                    "Quick Shell",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        ResultMessage = result.Message ?? "Workspace saved.";
        DialogResult = true;
        Close();
    }

    private static TerminalShortcut CloneShortcut(TerminalShortcut shortcut) =>
        new()
        {
            Id = shortcut.Id,
            Name = shortcut.Name,
            Abbreviation = shortcut.Abbreviation,
            Directory = shortcut.Directory,
            Command = shortcut.Command,
            Terminal = shortcut.Terminal,
            WtProfile = shortcut.WtProfile,
            RunAsAdmin = shortcut.RunAsAdmin,
            IsPinned = shortcut.IsPinned,
            PinOrder = shortcut.PinOrder,
            LastUsedUtc = shortcut.LastUsedUtc,
            Launches = shortcut.Launches.Select(launch => new WorkspaceEntry
            {
                Id = launch.Id,
                Label = launch.Label,
                Terminal = launch.Terminal,
                WtProfile = launch.WtProfile,
                Command = launch.Command,
                RunAsAdmin = launch.RunAsAdmin,
                IsEnabled = launch.IsEnabled,
                Order = launch.Order,
                TaskType = launch.TaskType,
            }).ToList(),
            DevServerUrl = shortcut.DevServerUrl,
            OpenDevServerOnLaunch = shortcut.OpenDevServerOnLaunch,
            RepoUrl = shortcut.RepoUrl,
            CompanionApps = shortcut.CompanionApps.Select(CompanionAppNormalization.CloneEntry).ToList(),
            CompanionAppPath = shortcut.CompanionAppPath,
            CompanionAppArguments = shortcut.CompanionAppArguments,
            OpenCompanionAppOnLaunch = shortcut.OpenCompanionAppOnLaunch,
        };

    private static void ApplyLaunchTarget(WorkspaceEntry launch, string launchTarget)
    {
        var scratch = new TerminalShortcut();
        TerminalCatalog.ApplyLaunchTargetId(scratch, launchTarget);
        launch.Terminal = scratch.Terminal;
        launch.WtProfile = scratch.WtProfile;
    }
    private sealed class CompanionRowUi
    {
        private readonly ShortcutWorkspaceEditorWindow _owner;
        private readonly string _entryId;
        private readonly ComboBox _presetBox;
        private readonly TextBox _pathBox;
        private readonly TextBox _argsBox;
        private readonly Button _browseButton;
        private readonly Button _addButton;
        private readonly Button _removeButton;
        private readonly TextBlock _label;
        public StackPanel Root { get; }
        public CompanionRowUi(ShortcutWorkspaceEditorWindow owner, CompanionAppFormRow model, int index, int totalCount)
        {
            _owner = owner;
            _entryId = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id;
            Root = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            _label = RunWpfUiHelpers.FieldLabel(
                index == 0 ? "Companion app" : $"Companion app {index + 1}",
                WorkspaceFormTooltips.CompanionAppPreset);
            Root.Children.Add(_label);
            var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _presetBox = new ComboBox
            {
                DisplayMemberPath = "Label",
                SelectedValuePath = "Id",
                MinWidth = 280,
                Margin = new Thickness(0, 0, 8, 0),
            };
            foreach (var choice in CompanionAppCatalog.GetInstalledFormChoices())
            {
                _presetBox.Items.Add(new { choice.Id, Label = choice.Title });
            }
            _presetBox.SelectedValue = CompanionAppCatalog.ToFormPresetValue(model.Preset, model.Path);
            _presetBox.SelectionChanged += (_, _) => ApplyPresetSelection();
            pickerRow.Children.Add(_presetBox);
            _argsBox = new TextBox
            {
                Text = model.Arguments,
                MinWidth = 120,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = CompanionAppArgumentValidation.FieldLabel,
            };
            pickerRow.Children.Add(_argsBox);
            _browseButton = new Button { Content = "Browse…", MinWidth = 72, Margin = new Thickness(0, 0, 4, 0) };
            _browseButton.Click += (_, _) => BrowseExecutable();
            pickerRow.Children.Add(_browseButton);
            _addButton = new Button
            {
                Content = "+",
                MinWidth = 32,
                ToolTip = CompanionAppFormEditor.AddTooltip,
                Margin = new Thickness(0, 0, 4, 0),
            };
            _addButton.Click += (_, _) => _owner.OnAddCompanionRow();
            pickerRow.Children.Add(_addButton);
            _removeButton = new Button
            {
                Content = "−",
                MinWidth = 32,
                ToolTip = CompanionAppFormEditor.RemoveTooltip,
            };
            _removeButton.Click += (_, _) => _owner.OnRemoveCompanionRow(this);
            pickerRow.Children.Add(_removeButton);
            Root.Children.Add(pickerRow);
            // Custom apps still need an editable path; catalog presets show the exe as the dropdown tooltip.
            _pathBox = new TextBox
            {
                Text = model.Path,
                Margin = new Thickness(0, 0, 0, 4),
                MinWidth = 360,
            };
            Root.Children.Add(_pathBox);
            ApplyPresetSelection(preservePath: true);
            RefreshChrome(index, totalCount);
        }
        public void RefreshChrome(int index, int totalCount)
        {
            _label.Text = index == 0 ? "Companion app" : $"Companion app {index + 1}";
            _addButton.Visibility = index == totalCount - 1 && totalCount < CompanionAppFormEditor.MaxCount
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            _removeButton.Visibility = totalCount > 1
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }
        public CompanionAppFormRow ToFormRow()
        {
            var preset = _presetBox.SelectedValue as string ?? CompanionAppCatalog.PresetNone;
            var path = _pathBox.Text ?? string.Empty;
            var arguments = _argsBox.Text ?? string.Empty;
            // Selecting a companion implies open-on-launch (same as CmdPal preset reconcile).
            var state = CompanionAppCatalog.ReconcileForForm(preset, path, arguments);
            return new CompanionAppFormRow
            {
                Id = _entryId,
                Preset = state.Preset,
                Path = state.Path,
                Arguments = string.IsNullOrWhiteSpace(state.Arguments) ? arguments : state.Arguments,
                OpenOnLaunch = state.LaunchOnWorkspaceOpen,
            };
        }
        private void BrowseExecutable()
        {
            if (!RunFileDialogs.TryPickExecutable(_owner, out var selected))
            {
                return;
            }
            var preset = CompanionAppCatalog.ResolvePresetAfterBrowse(selected);
            _presetBox.SelectedValue = preset;
            _pathBox.Text = selected;
            ApplyPresetSelection(preservePath: true);
        }
        private void ApplyPresetSelection(bool preservePath = false)
        {
            var preset = _presetBox.SelectedValue as string ?? CompanionAppCatalog.PresetNone;
            var isCustom = string.Equals(preset, CompanionAppCatalog.PresetCustom, StringComparison.OrdinalIgnoreCase);
            var isNone = string.Equals(preset, CompanionAppCatalog.PresetNone, StringComparison.OrdinalIgnoreCase);
            if (isNone)
            {
                if (!preservePath)
                {
                    _pathBox.Text = string.Empty;
                    _argsBox.Text = string.Empty;
                }
            }
            else if (!preservePath
                     && CompanionAppCatalog.IsCatalogPreset(preset)
                     && CompanionAppCatalog.TryApplyPreset(preset, out var path, out var args))
            {
                _pathBox.Text = path ?? string.Empty;
                _argsBox.Text = args;
            }
            _pathBox.IsReadOnly = !isCustom;
            _pathBox.Visibility = isCustom
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            _argsBox.Visibility = CompanionAppArgumentValidation.ShouldShowArgumentsField(preset, _pathBox.Text)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            _presetBox.ToolTip = CompanionAppCatalog.ShouldShowExecutablePath(_pathBox.Text)
                ? _pathBox.Text
                : WorkspaceFormTooltips.CompanionAppPreset;
            _browseButton.IsEnabled = isCustom;
        }
    }
    private sealed class LaunchRow
    {
        private readonly ShortcutWorkspaceEditorWindow _owner;
        private readonly string _entryId;
        private readonly string _label;
        private readonly bool _isEnabled;
        private string _taskType;
        private readonly LaunchRowKind _kind;
        private readonly TextBox? _commandBox;
        private readonly ComboBox _terminalBox;
        private readonly CheckBox _adminBox;

        public LaunchRow(
            ShortcutWorkspaceEditorWindow owner,
            WorkspaceEntry? launch,
            string? entryId,
            LaunchRowKind kind,
            string label,
            string command,
            string launchTarget,
            bool runAsAdmin,
            bool isEnabled,
            string taskType,
            int order)
        {
            _ = order;
            _owner = owner;
            _entryId = entryId ?? launch?.Id ?? Guid.NewGuid().ToString("N");
            _label = label;
            _isEnabled = isEnabled;
            _taskType = TaskTypeCatalog.Normalize(taskType);
            _kind = kind;

            // CmdPal-style compact row: command (or Open in terminal) | profile | Admin | Remove
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            UIElement commandCell;
            if (kind == LaunchRowKind.OpenInTerminal)
            {
                var openLabel = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 6, 8, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = owner._launchText.OpenInTerminal,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "Opens a terminal in the workspace folder without running a command.",
                    },
                };
                commandCell = openLabel;
                _commandBox = null;
            }
            else
            {
                _commandBox = new TextBox
                {
                    Text = command,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Shell command to run after opening the terminal in the workspace folder.",
                };
                _commandBox.TextChanged += (_, _) => _owner.RefreshSuggestionPanel();
                commandCell = _commandBox;
            }

            Grid.SetColumn(commandCell, 0);
            grid.Children.Add(commandCell);

            _terminalBox = new ComboBox
            {
                DisplayMemberPath = "Label",
                SelectedValuePath = "Id",
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = FormActionGlyphs.TerminalProfileTooltip,
            };
            foreach (var choice in RunTerminalChoices.GetLaunchTargetChoices(_owner._catalog))
            {
                _terminalBox.Items.Add(new { choice.Id, choice.Label });
            }

            _terminalBox.SelectedValue = launchTarget;
            Grid.SetColumn(_terminalBox, 2);
            grid.Children.Add(_terminalBox);

            _adminBox = new CheckBox
            {
                Content = "Admin",
                IsChecked = runAsAdmin,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = WorkspaceFormTooltips.RunAsAdmin,
            };
            Grid.SetColumn(_adminBox, 4);
            grid.Children.Add(_adminBox);

            var remove = new Button
            {
                Content = FormActionGlyphs.RemoveLabel,
                MinWidth = 72,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = owner._launchText.RemoveTooltip,
            };
            remove.Click += (_, _) => _owner.RemoveLaunchRow(this);
            Grid.SetColumn(remove, 6);
            grid.Children.Add(remove);

            Root = grid;
        }

        public FrameworkElement Root { get; }

        public string CommandText => _commandBox?.Text ?? string.Empty;

        public string LabelText => _label;

        public LaunchRowKind Kind => _kind;

        public void ApplyPill(CommandSuggestionPill pill)
        {
            if (_commandBox is null)
            {
                return;
            }

            _commandBox.Text = pill.Command;
            _taskType = pill.TaskType;
        }

        public RunLaunchRowSnapshot CaptureSnapshot() =>
            new(
                _entryId,
                _kind,
                _label,
                CommandText,
                _taskType,
                _terminalBox.SelectedValue as string ?? "default",
                _adminBox.IsChecked == true,
                _isEnabled,
                IsEditorPlaceholder: false);

        public LaunchRowDraft ToLaunchRowDraft() =>
            new()
            {
                Kind = _kind,
                Id = _entryId,
                Label = _label,
                Command = CommandText,
                TaskType = _taskType,
                LaunchTarget = _terminalBox.SelectedValue as string ?? "default",
                RunAsAdmin = _adminBox.IsChecked == true,
                IsEnabled = _isEnabled,
                IsEditorPlaceholder = false,
            };
    }
}

internal static class ShortcutEditor
{
    public static bool TryShowDialog(
        TerminalShortcut? existing,
        IShortcutRepository shortcuts,
        IWorkspaceEditorFactory editorFactory,
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        IWorkspaceGitOperations gitOperations,
        IWorktreeBranchTargetStore targetStore,
        ITerminalCatalog catalog,
        out string message)
    {
        message = string.Empty;
        var saved = false;
        var resultMessage = string.Empty;

        void Show()
        {
            var window = new ShortcutWorkspaceEditorWindow(
                existing,
                shortcuts,
                editorFactory,
                projectAnalysis,
                commandSuggestions,
                gitOperations,
                targetStore,
                catalog);
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
