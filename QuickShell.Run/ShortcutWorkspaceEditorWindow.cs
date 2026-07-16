using QuickShell.Models;

using QuickShell.Classification;

using QuickShell.Services;

using System.IO;

using System.Windows.Input;

using System.Windows;

using System.Windows.Controls;



namespace QuickShell.Run;



internal sealed class ShortcutWorkspaceEditorWindow : Window

{

    private readonly TerminalShortcut? _existing;

    private readonly IShortcutRepository _shortcuts;

    private readonly TerminalShortcut _working;

    private readonly TextBox _nameBox;

    private readonly TextBox _abbreviationBox;

    private readonly TextBox _directoryBox;

    private readonly ComboBox _targetBranchBox;

    private readonly StackPanel _launchesPanel;

    private readonly TextBox _devServerUrlBox;

    private readonly CheckBox _openDevServerBox;

    private readonly TextBox _repoUrlBox;

    private readonly StackPanel _companionRowsHost = new() { Margin = new Thickness(0, 0, 0, 8) };

    private readonly List<CompanionRowUi> _companionRows = [];

    private readonly List<LaunchRow> _launchRows = [];

    private readonly RunLaunchSuggestionPanel _suggestionPanel = new();

    private readonly FormEditHistory<List<RunLaunchRowSnapshot>> _editHistory =
        new(snapshot => snapshot.Select(entry => entry with { }).ToList());

    private RunDirectorySuggestionLoader? _suggestionLoader;

    private int _activeSuggestionGeneration;



    public string ResultMessage { get; private set; } = string.Empty;



    public ShortcutWorkspaceEditorWindow(TerminalShortcut? existing, IShortcutRepository shortcuts)

    {

        _existing = existing;

        _shortcuts = shortcuts;

        _working = existing is null ? new TerminalShortcut() : CloneShortcut(existing);



        ShortcutLaunchNormalization.EnsureLaunchesFromLegacy(_working);

        if (_working.Launches.Count == 0)

        {

            _working.Launches.Add(CreateLaunchEntry("Launch", string.Empty, "default", false, 0));

        }



        Title = existing is null ? "Create Quick Shell workspace" : $"Edit {_working.Name}";

        Width = 680;

        MinHeight = 560;

        WindowStartupLocation = WindowStartupLocation.CenterScreen;



        var root = new DockPanel { Margin = new Thickness(16) };

        var tabs = new TabControl();

        RunWpfUiHelpers.EnableTabKeyboardNavigation(tabs);



        var general = new StackPanel { Margin = new Thickness(8) };

        general.Children.Add(RunWpfUiHelpers.FieldLabel("Name", WorkspaceFormTooltips.Name));

        _nameBox = new TextBox { Text = _working.Name, Margin = new Thickness(0, 0, 0, 8) };

        general.Children.Add(_nameBox);



        general.Children.Add(RunWpfUiHelpers.FieldLabel(

            "Home keyword (optional)",

            WorkspaceFormTooltips.HomeKeyword));

        _abbreviationBox = new TextBox

        {

            Text = _working.Abbreviation ?? string.Empty,

            Margin = new Thickness(0, 0, 0, 4),

            ToolTip = WorkspaceFormTooltips.HomeKeywordExample,

        };

        general.Children.Add(_abbreviationBox);



        general.Children.Add(RunWpfUiHelpers.FieldLabel("Folder", WorkspaceFormTooltips.Directory));

        _directoryBox = new TextBox { Text = _working.Directory, Margin = new Thickness(0, 0, 0, 4) };

        general.Children.Add(_directoryBox);

        general.Children.Add(CreateBrowseFolderButton());



        general.Children.Add(RunWpfUiHelpers.FieldLabel(

            "Target branch (optional)",

            "Git branch to switch to when this workspace launches. Pick from local branches or type a branch name."));

        _targetBranchBox = CreateEditableCombo();

        general.Children.Add(_targetBranchBox);

        ReloadBranchChoices(_working.Directory, WorktreeBranchTargetStore.GetTargetForDirectory(_working.Directory));



        tabs.Items.Add(RunWpfUiHelpers.CreateTab("_General", general));



        var launches = new StackPanel { Margin = new Thickness(8) };

        launches.Children.Add(new TextBlock

        {

            Text = "Each launch opens a terminal tab or window. Disable a launch to keep it saved without running it.",

            TextWrapping = TextWrapping.Wrap,

            Foreground = System.Windows.Media.Brushes.Gray,

            Margin = new Thickness(0, 0, 0, 8),

        });

        _suggestionPanel.PillClicked += HandleSuggestionPillClicked;

        launches.Children.Add(_suggestionPanel.Root);

        _launchesPanel = new StackPanel();

        launches.Children.Add(_launchesPanel);

        var addLaunch = new Button

        {

            Content = "Add launch",

            HorizontalAlignment = HorizontalAlignment.Left,

            Margin = new Thickness(0, 8, 0, 0),

        };

        addLaunch.Click += (_, _) => AddLaunchRow();

        launches.Children.Add(addLaunch);

        LoadLaunchRows();

        tabs.Items.Add(RunWpfUiHelpers.CreateTab("_Launches", launches));



        var links = new StackPanel { Margin = new Thickness(8) };

        links.Children.Add(RunWpfUiHelpers.FieldLabel("Dev server URL", WorkspaceFormTooltips.DevServerUrl));

        _devServerUrlBox = new TextBox

        {

            Text = _working.DevServerUrl ?? string.Empty,

            Margin = new Thickness(0, 0, 0, 4),

            ToolTip = WorkspaceFormTooltips.DevServerUrlExample,

        };

        links.Children.Add(_devServerUrlBox);

        _openDevServerBox = new CheckBox

        {

            Content = "Open dev server on launch",

            IsChecked = _working.OpenDevServerOnLaunch,

            Margin = new Thickness(0, 0, 0, 8),

            ToolTip = WorkspaceFormTooltips.DevServerOnLaunch,

        };

        links.Children.Add(_openDevServerBox);



        links.Children.Add(RunWpfUiHelpers.FieldLabel("Repository URL", WorkspaceFormTooltips.RepoUrl));

        _repoUrlBox = new TextBox

        {

            Text = _working.RepoUrl ?? string.Empty,

            Margin = new Thickness(0, 0, 0, 8),

            ToolTip = WorkspaceFormTooltips.RepoUrlExample,

        };

        links.Children.Add(_repoUrlBox);



        links.Children.Add(_companionRowsHost);
        RebuildCompanionRows(CompanionAppFormEditor.FromShortcut(_working));

        tabs.Items.Add(RunWpfUiHelpers.CreateTab("_Links", links));



        DockPanel.SetDock(tabs, Dock.Top);

        root.Children.Add(tabs);



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



        _suggestionLoader = new RunDirectorySuggestionLoader(Dispatcher);

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



    private void ReloadBranchChoices(string directory, string? selectedBranch)

    {

        _targetBranchBox.Items.Clear();

        foreach (var branch in WorkspaceGitOperations.ListLocalBranches(directory))

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

        _directoryBox.Text = directory;

        if (string.IsNullOrWhiteSpace(_nameBox.Text))

        {

            _nameBox.Text = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        }



        if (string.IsNullOrWhiteSpace(_repoUrlBox.Text))

        {

            _repoUrlBox.Text = GitRepoDiscovery.TryGetRemoteUrl(directory) ?? string.Empty;

        }



        if (string.IsNullOrWhiteSpace(_devServerUrlBox.Text))

        {

            _devServerUrlBox.Text = ProjectAnalysisAccessor.Instance.TryDetectDevServerUrl(directory) ?? string.Empty;

        }



        ReloadBranchChoices(directory, WorktreeBranchTargetStore.GetTargetForDirectory(directory));

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



    private void LoadLaunchRows()

    {

        _launchesPanel.Children.Clear();

        _launchRows.Clear();

        var order = 0;

        foreach (var launch in _working.Launches.OrderBy(entry => entry.Order))

        {

            AddLaunchRow(launch, order++);

        }



        while (_launchRows.Count < LaunchRowListEditor.MinimumEditorRowCount)

        {

            AddLaunchRow(isEditorPlaceholder: true);

        }

    }



    private void AddLaunchRow() => AddLaunchRow(isEditorPlaceholder: false);



    private void AddLaunchRow(bool isEditorPlaceholder) =>

        AddLaunchRow(null, _launchRows.Count, isEditorPlaceholder);



    private void AddLaunchRow(WorkspaceEntry? launch, int order) =>

        AddLaunchRow(launch, order, isEditorPlaceholder: false);



    private void AddLaunchRow(WorkspaceEntry? launch, int order, bool isEditorPlaceholder)

    {

        var row = new LaunchRow(

            this,

            launch,

            launch?.Label ?? $"Launch {order + 1}",

            launch?.Command ?? string.Empty,

            launch is null ? "default" : ShortcutFormSave.EncodeLaunchTargetForEntry(launch),

            launch?.RunAsAdmin ?? false,

            launch?.IsEnabled ?? true,

            TaskTypeCatalog.Normalize(launch?.TaskType),

            order,

            isEditorPlaceholder);

        _launchRows.Add(row);

        _launchesPanel.Children.Add(row.Root);

    }



    private void RemoveLaunchRow(LaunchRow row)

    {

        _launchRows.Remove(row);

        _launchesPanel.Children.Remove(row.Root);

    }



    private void RefreshSuggestionPanel()

    {

        if (_suggestionLoader is null)

        {

            return;

        }



        var directory = _directoryBox.Text.Trim();

        var usedCommands = _launchRows.Select(row => row.CommandText).ToList();

        _suggestionLoader.Schedule(

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

            row.IsEditorPlaceholder

            && string.IsNullOrWhiteSpace(row.CommandText));

        if (target is null)

        {

            AddLaunchRow();

            target = _launchRows[^1];

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



    private void RestoreSnapshot(IReadOnlyList<RunLaunchRowSnapshot> snapshots)

    {

        _launchesPanel.Children.Clear();

        _launchRows.Clear();

        for (var i = 0; i < snapshots.Count; i++)

        {

            var snapshot = snapshots[i];

            AddLaunchRow(

                snapshot.Label,

                snapshot.Command,

                snapshot.LaunchTarget,

                snapshot.RunAsAdmin,

                snapshot.IsEnabled,

                snapshot.TaskType,

                i,

                snapshot.IsEditorPlaceholder);

        }

    }



    private void AddLaunchRow(

        string label,

        string command,

        string launchTarget,

        bool runAsAdmin,

        bool isEnabled,

        string taskType,

        int order,

        bool isEditorPlaceholder = false)

    {

        var row = new LaunchRow(

            this,

            null,

            label,

            command,

            launchTarget,

            runAsAdmin,

            isEnabled,

            taskType,

            order,

            isEditorPlaceholder);

        _launchRows.Add(row);

        _launchesPanel.Children.Add(row.Root);

    }



    private void SaveWorkspace()

    {

        _working.Name = _nameBox.Text.Trim();

        _working.Abbreviation = string.IsNullOrWhiteSpace(_abbreviationBox.Text) ? null : _abbreviationBox.Text.Trim();

        _working.Directory = _directoryBox.Text.Trim();

        _working.DevServerUrl = string.IsNullOrWhiteSpace(_devServerUrlBox.Text) ? null : _devServerUrlBox.Text.Trim();

        _working.OpenDevServerOnLaunch = _openDevServerBox.IsChecked == true;

        _working.RepoUrl = string.IsNullOrWhiteSpace(_repoUrlBox.Text) ? null : _repoUrlBox.Text.Trim();



        _working.CompanionApps = CompanionAppFormEditor.ToCompanionEntries(CaptureCompanionFormRows());
        _working.CompanionAppPath = null;
        _working.CompanionAppArguments = null;
        _working.OpenCompanionAppOnLaunch = false;
        CompanionAppNormalization.NormalizeCompanions(_working);



        var rowsToSave = _launchRows

            .Where(row => row.ShouldPersist())

            .ToList();

        if (rowsToSave.Count == 0)

        {

            rowsToSave = [_launchRows[0]];

        }



        _working.Launches = rowsToSave

            .Select((row, index) => row.ToEntry(index))

            .ToList();

        ShortcutLaunchNormalization.MirrorLegacyFieldsFromFirstLaunch(_working);

        ShortcutLaunchNormalization.NormalizeShortcut(_working);



        if (!ShortcutValidation.TryValidate(_working, out var validationError))

        {

            MessageBox.Show(this, validationError, "Quick Shell", MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }



        var targetBranch = _targetBranchBox.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(targetBranch)

            && WorkspaceGitOperations.TryResolveWorktreeKey(_working.Directory, out _))

        {

            var branches = WorkspaceGitOperations.ListLocalBranches(_working.Directory);

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



        if (!WorktreeBranchTargetStore.TrySetTargetForDirectory(

                _working.Directory,

                string.IsNullOrWhiteSpace(targetBranch) ? null : targetBranch,

                out var branchError))

        {

            if (!string.IsNullOrWhiteSpace(targetBranch))

            {

                MessageBox.Show(this, branchError ?? "Could not save target branch.", "Quick Shell", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }

        }



        try

        {

            var resolvedName = _shortcuts.ResolveAvailableName(_working.Name, _existing?.Name);

            _working.Name = resolvedName;

            _shortcuts.Upsert(_working, _existing?.Name);

            ResultMessage = _existing is null

                ? $"Created workspace '{resolvedName}'."

                : $"Updated workspace '{resolvedName}'.";

            DialogResult = true;

            Close();

        }

        catch (Exception ex)

        {

            MessageBox.Show(this, ex.Message, "Quick Shell", MessageBoxButton.OK, MessageBoxImage.Error);

        }

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



    private static WorkspaceEntry CreateLaunchEntry(

        string label,

        string command,

        string launchTarget,

        bool runAsAdmin,

        int order)

    {

        var entry = new WorkspaceEntry

        {

            Id = Guid.NewGuid().ToString("N"),

            Label = label,

            Command = command,

            RunAsAdmin = runAsAdmin,

            IsEnabled = true,

            Order = order,

            TaskType = TaskTypeCatalog.None,

        };

        ApplyLaunchTarget(entry, launchTarget);

        return entry;

    }



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
        private readonly CheckBox _openBox;
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

            _pathBox = new TextBox
            {
                Text = model.Path,
                Margin = new Thickness(0, 0, 0, 4),
                MinWidth = 360,
            };
            Root.Children.Add(_pathBox);

            _argsBox = new TextBox
            {
                Text = model.Arguments,
                Margin = new Thickness(0, 0, 0, 4),
            };
            Root.Children.Add(_argsBox);

            _openBox = new CheckBox
            {
                Content = "Open on workspace launch",
                IsChecked = model.OpenOnLaunch,
            };
            Root.Children.Add(_openBox);

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
            return new CompanionAppFormRow
            {
                Id = _entryId,
                Preset = preset,
                Path = _pathBox.Text ?? string.Empty,
                Arguments = _argsBox.Text ?? string.Empty,
                OpenOnLaunch = _openBox.IsChecked == true,
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

                _openBox.IsChecked = false;
            }
            else if (!preservePath
                     && CompanionAppCatalog.IsCatalogPreset(preset)
                     && CompanionAppCatalog.TryApplyPreset(preset, out var path, out var args))
            {
                _pathBox.Text = path ?? string.Empty;
                _argsBox.Text = args;
                _openBox.IsChecked = true;
            }

            _pathBox.IsReadOnly = !isCustom;
            _browseButton.IsEnabled = isCustom;
            _openBox.IsEnabled = !isNone;
        }
    }

    private sealed class LaunchRow

    {

        private readonly ShortcutWorkspaceEditorWindow _owner;

        private readonly string _entryId;

        private string _taskType;

        private bool _isEditorPlaceholder;



        public LaunchRow(

            ShortcutWorkspaceEditorWindow owner,

            WorkspaceEntry? launch,

            string label,

            string command,

            string launchTarget,

            bool runAsAdmin,

            bool isEnabled,

            string taskType,

            int order,

            bool isEditorPlaceholder = false)

        {

            _owner = owner;

            _entryId = launch?.Id ?? Guid.NewGuid().ToString("N");

            _taskType = TaskTypeCatalog.Normalize(taskType);

            _isEditorPlaceholder = isEditorPlaceholder;



            var card = new Border

            {

                BorderBrush = System.Windows.Media.Brushes.Gray,

                BorderThickness = new Thickness(1),

                CornerRadius = new CornerRadius(4),

                Padding = new Thickness(10),

                Margin = new Thickness(0, 0, 0, 10),

            };



            var body = new StackPanel();

            body.Children.Add(LabeledField(

                "Launch label",

                "Shown in multi-launch menus. Example: API, Web, or Tests.",

                out var labelBox,

                label));

            body.Children.Add(LabeledField(

                "Command (optional)",

                "Shell command to run after opening the terminal in the workspace folder.",

                out var commandBox,

                command));



            commandBox.TextChanged += (_, _) => _owner.RefreshSuggestionPanel();



            body.Children.Add(new TextBlock { Text = "Terminal profile", Margin = new Thickness(0, 8, 0, 4) });

            var terminalBox = new ComboBox

            {

                DisplayMemberPath = "Label",

                SelectedValuePath = "Id",

                Margin = new Thickness(0, 0, 0, 8),

            };

            foreach (var choice in RunTerminalChoices.GetLaunchTargetChoices())

            {

                terminalBox.Items.Add(new { choice.Id, choice.Label });

            }



            terminalBox.SelectedValue = launchTarget;

            body.Children.Add(terminalBox);



            var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            var enabledBox = new CheckBox

            {

                Content = "Include in workspace launch",

                IsChecked = isEnabled,

                Margin = new Thickness(0, 0, 16, 0),

                ToolTip = "Turn off to keep this launch saved without running it when you open the workspace.",

            };

            var adminBox = new CheckBox

            {

                Content = "Launch elevated",

                IsChecked = runAsAdmin,

                ToolTip = WorkspaceFormTooltips.RunAsAdmin,

            };

            toggles.Children.Add(enabledBox);

            toggles.Children.Add(adminBox);

            body.Children.Add(toggles);



            var clear = new Button

            {

                Content = "Clear command",

                HorizontalAlignment = HorizontalAlignment.Left,

            };

            clear.Click += (_, _) =>

            {

                _owner.PushFormEditSnapshot();

                CommandBox.Text = string.Empty;

                _taskType = TaskTypeCatalog.None;

                _isEditorPlaceholder = false;

                _owner.RefreshSuggestionPanel();

            };

            body.Children.Add(clear);



            card.Child = body;

            Root = card;

            LabelBox = labelBox;

            CommandBox = commandBox;

            TerminalBox = terminalBox;

            AdminBox = adminBox;

            EnabledBox = enabledBox;

            Order = order;

        }



        public Border Root { get; }



        private TextBox LabelBox { get; }

        private TextBox CommandBox { get; }

        private ComboBox TerminalBox { get; }

        private CheckBox AdminBox { get; }

        private CheckBox EnabledBox { get; }

        private int Order { get; set; }



        public string CommandText => CommandBox.Text;



        public void ApplyPill(CommandSuggestionPill pill)

        {

            CommandBox.Text = pill.Command;

            _taskType = pill.TaskType;

            _isEditorPlaceholder = false;

        }



        public bool IsEditorPlaceholder => _isEditorPlaceholder;



        public bool ShouldPersist()

        {

            if (_isEditorPlaceholder

                && string.IsNullOrWhiteSpace(CommandBox.Text)

                && string.Equals(_taskType, TaskTypeCatalog.None, StringComparison.Ordinal))

            {

                return false;

            }



            return true;

        }



        public RunLaunchRowSnapshot CaptureSnapshot() =>

            new(

                LabelBox.Text,

                CommandBox.Text,

                _taskType,

                TerminalBox.SelectedValue as string ?? "default",

                AdminBox.IsChecked == true,

                EnabledBox.IsChecked == true,

                _isEditorPlaceholder);



        public LaunchRowDraft ToLaunchRowDraft() =>

            new()

            {

                Command = CommandBox.Text,

                TaskType = _taskType,

                LaunchTarget = TerminalBox.SelectedValue as string ?? "default",

                IsEditorPlaceholder = _isEditorPlaceholder,

            };



        public WorkspaceEntry ToEntry(int order)

        {

            var entry = new WorkspaceEntry

            {

                Id = _entryId,

                Label = LabelBox.Text.Trim(),

                Command = string.IsNullOrWhiteSpace(CommandBox.Text) ? null : CommandBox.Text.Trim(),

                RunAsAdmin = AdminBox.IsChecked == true,

                IsEnabled = EnabledBox.IsChecked == true,

                Order = order,

                TaskType = _taskType,

            };

            ApplyLaunchTarget(entry, TerminalBox.SelectedValue as string ?? "default");

            return entry;

        }



        private static StackPanel LabeledField(string label, string tooltip, out TextBox box, string value)

        {

            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4), ToolTip = tooltip };

            box = new TextBox { Text = value };

            panel.Children.Add(caption);

            panel.Children.Add(box);

            return panel;

        }

    }

}



internal static class ShortcutEditor

{

    public static bool TryShowDialog(TerminalShortcut? existing, IShortcutRepository shortcuts, out string message)

    {

        message = string.Empty;

        var saved = false;

        var resultMessage = string.Empty;



        void Show()

        {

            var window = new ShortcutWorkspaceEditorWindow(existing, shortcuts);

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


