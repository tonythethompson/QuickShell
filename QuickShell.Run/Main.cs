using System.IO;
using System.Windows.Controls;
using System.Windows;
using ManagedCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerToys.Settings.UI.Library;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Services;
using QuickShell.Services.WorkspaceEditor;
using Wox.Plugin;

namespace QuickShell.Run;

public class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable
{
    public const string PluginIdValue = "a7c3e891-4b2d-4f6e-9c1a-2d8e5f03b4c6";

    public static string PluginID => PluginIdValue;

    static Main()
    {
        // Prefer ModuleInitializer; keep as belt-and-suspenders for hosts that skip it.
        PluginDependencyResolver.EnsureRegistered();
    }

    private PluginInitContext? _context;
    private string _iconPath = string.Empty;
    private ServiceProvider? _serviceProvider;
    private IQuickShellLifetime? _lifetime;
    private IShortcutRepository? _shortcuts;
    private IWorkspaceLaunchService? _workspaceLaunch;
    private QuickShellSettingsReader? _settings;
    private QuickShellRunSettingsPanel? _settingsPanel;
    private IProjectAnalysisService? _projectAnalysis;
    private ICommandSuggestionService? _commandSuggestions;
    private IWorkspaceEditorFactory? _editorFactory;
    private IWorkspaceHealthChecker? _healthChecker;
    private IWorkspaceGitOperations? _gitOperations;
    private IWorktreeBranchTargetStore? _targetStore;
    private ITerminalCatalog? _terminalCatalog;
    private ITerminalLaunchGlyphs? _terminalLaunchGlyphs;
    private string _lastQuery = string.Empty;
    private bool _disposed;

    public string Name => "Quick Shell";

    public string Description => "Open saved folders in any terminal you use";

    public string GetTranslatedPluginTitle() => Name;

    public string GetTranslatedPluginDescription() => Description;

    public void Init(PluginInitContext context)
    {
        using var startupTrace = StartupPerformanceTrace.Measure("Run plugin init");
        using (StartupPerformanceTrace.Measure("Run services setup"))
        {
            var collection = new ServiceCollection();
            collection.AddQuickShellCore(lifetime: new QuickShellLifetime());
            _serviceProvider = collection.BuildServiceProvider();
            _lifetime = _serviceProvider.GetRequiredService<IQuickShellLifetime>();
            _shortcuts = _serviceProvider.GetRequiredService<IShortcutRepository>();
            _workspaceLaunch = _serviceProvider.GetRequiredService<IWorkspaceLaunchService>();
            _projectAnalysis = _serviceProvider.GetRequiredService<IProjectAnalysisService>();
            _commandSuggestions = _serviceProvider.GetRequiredService<ICommandSuggestionService>();
            _editorFactory = _serviceProvider.GetRequiredService<IWorkspaceEditorFactory>();
            _healthChecker = _serviceProvider.GetRequiredService<IWorkspaceHealthChecker>();
            _gitOperations = _serviceProvider.GetRequiredService<IWorkspaceGitOperations>();
            _targetStore = _serviceProvider.GetRequiredService<IWorktreeBranchTargetStore>();
            _terminalCatalog = _serviceProvider.GetRequiredService<ITerminalCatalog>();
            _terminalLaunchGlyphs = _serviceProvider.GetRequiredService<ITerminalLaunchGlyphs>();
            _settings = _serviceProvider.GetRequiredService<QuickShellSettingsReader>();
            _context = context;
            UpdateIconPath(context.API.GetCurrentTheme());
            context.API.ThemeChanged += OnThemeChanged;
        }

        using (StartupPerformanceTrace.Measure("Run shortcut preload kickoff"))
        {
            if (_shortcuts is ShortcutRepository repository && _lifetime is not null)
            {
                BeginShortcutPreload(repository, _lifetime);
            }
        }
    }

    private static void BeginShortcutPreload(ShortcutRepository shortcuts, IQuickShellLifetime lifetime) =>
        _ = PreloadShortcutsAsync(shortcuts, lifetime);

    private static async Task PreloadShortcutsAsync(ShortcutRepository shortcuts, IQuickShellLifetime lifetime)
    {
        try
        {
            await shortcuts.PreloadAsync(lifetime.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort warm-up; synchronous queries still load on demand.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime?.Cancel();

        if (_context?.API is not null)
        {
            _context.API.ThemeChanged -= OnThemeChanged;
        }

        _serviceProvider?.Dispose();
        _serviceProvider = null;
        _lifetime = null;
        _shortcuts = null;
        _workspaceLaunch = null;
        GC.SuppressFinalize(this);
    }

    public void ReloadData()
    {
        Shortcuts.Reload();
        _settingsPanel?.Reload();
    }

    public Control CreateSettingPanel()
    {
        _settingsPanel ??= new QuickShellRunSettingsPanel(
            Settings,
            Shortcuts,
            EditorFactory,
            ProjectAnalysis,
            CommandSuggestions,
            GitOperations,
            TargetStore,
            TerminalCatalog,
            (_, _) => { });
        _settingsPanel.Reload();
        return _settingsPanel;
    }

    public IEnumerable<PluginAdditionalOption> AdditionalOptions => [];

    public void UpdateSettings(PowerLauncherPluginSettings settings) =>
        _settingsPanel?.UpdateSettings(settings);

    private IShortcutRepository Shortcuts =>
        _shortcuts ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private QuickShellSettingsReader Settings =>
        _settings ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IWorkspaceLaunchService WorkspaceLaunch =>
        _workspaceLaunch ?? throw new InvalidOperationException("Quick Shell launch service is not initialized.");

    public List<Result> Query(Query query)
    {
        _lastQuery = query.RawQuery;
        var hasActionKeyword = !string.IsNullOrEmpty(query.ActionKeyword);
        var search = query.Search?.Trim() ?? string.Empty;
        var rawQuery = query.RawQuery?.Trim() ?? string.Empty;

        var activation = new QueryActivationContext(hasActionKeyword, string.IsNullOrWhiteSpace(search) ? rawQuery : search);
        if (RunGlobalQuery.ShouldSuppressEmptyGlobalQuery(activation))
        {
            return [];
        }

        var directActivationBrowse = hasActionKeyword;
        var filter = search;
        if (!hasActionKeyword && RunGlobalQuery.TryActivate(search, rawQuery, out var remainingSearch))
        {
            directActivationBrowse = true;
            filter = remainingSearch;
        }

        var results = new List<Result>();

        if (directActivationBrowse)
        {
            results.AddRange(GetManageResults(filter));
        }

        IEnumerable<TerminalShortcut> shortcuts;
        try
        {
            shortcuts = string.IsNullOrWhiteSpace(filter)
                ? Shortcuts.GetShortcuts()
                : MergeSearchResults(filter);
        }
        catch (TimeoutException)
        {
            // The shortcut store lock was stuck; degrade to no matches rather than
            // surfacing a host error on a PowerToys Run keystroke.
            shortcuts = [];
        }

        results.AddRange(shortcuts
            .Select(shortcut => CreateShortcutResult(shortcut, filter, directActivationBrowse)));

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not RunContextData contextData)
        {
            return [];
        }

        return contextData.Kind switch
        {
            RunContextKind.Manage => [],
            RunContextKind.Shortcut => BuildShortcutContextMenus(contextData.ShortcutId),
            _ => [],
        };
    }

    private List<ContextMenuResult> BuildShortcutContextMenus(string? shortcutId)
    {
        if (string.IsNullOrWhiteSpace(shortcutId))
        {
            return [];
        }

        var shortcut = Shortcuts.GetById(shortcutId);
        if (shortcut is null)
        {
            return [];
        }

        if (ShortcutHealth.WouldNeedRepair(shortcut))
        {
            var repairMenus = new List<ContextMenuResult>
            {
                CreateContextMenu("Edit shortcut", "\uE70F", _ =>
                {
                    ExecuteManageShortcutEdit(shortcut);
                    return false;
                }),
            };
            if (shortcut.IsPinned)
            {
                repairMenus.Add(CreateContextMenu("Unfavorite", "\uE735", _ =>
                {
                    Shortcuts.TogglePinned(shortcut.Name);
                    NotifyStatus($"Removed '{shortcut.Name}' from favorites.");
                    RefreshResults();
                    return false;
                }));
            }
            repairMenus.Add(CreateContextMenu("Delete shortcut", "\uE74D", _ =>
            {
                if (!Shortcuts.Delete(shortcut.Name))
                {
                    return false;
                }

                NotifyStatus($"Deleted shortcut '{shortcut.Name}'.");
                RefreshResults();
                return false;
            }));
            return repairMenus;
        }

        var menus = new List<ContextMenuResult>();
        if (WorkspaceTrustFeatures.Enabled)
        {
            var storedWorkspace = Shortcuts.GetStoredWorkspace(shortcut.Id);
            if (storedWorkspace is not null)
            {
                if (storedWorkspace.Security.IsTrusted)
                {
                    menus.Add(CreateContextMenu("Revoke workspace trust", "\uE72E", _ =>
                    {
                        var transition = Shortcuts.RevokeTrust(shortcut.Id);
                        NotifyStatus(transition.Message);
                        RefreshResults();
                        return false;
                    }));
                }
                else
                {
                    menus.Add(CreateContextMenu("Trust workspace", "\uE72E", _ => TrustWorkspace(storedWorkspace)));
                }
            }
        }

        menus.AddRange(
        [
            CreateContextMenu("Edit shortcut", "\uE70F", _ =>
            {
                ExecuteManageShortcutEdit(shortcut);
                return false;
            }),
            CreateContextMenu("Duplicate shortcut", ShortcutGlyphs.Duplicate, _ =>
            {
                var duplicate = Shortcuts.BuildDuplicate(shortcut.Name);
                if (duplicate is null)
                {
                    return false;
                }

                if (ShortcutEditor.TryShowDialog(duplicate, Shortcuts, EditorFactory, ProjectAnalysis, CommandSuggestions, GitOperations, TargetStore, TerminalCatalog, out var message))
                {
                    NotifyStatus(message);
                    RefreshResults();
                }

                return false;
            }),
            CreateContextMenu("Delete shortcut", "\uE74D", _ =>
            {
                if (!Shortcuts.Delete(shortcut.Name))
                {
                    return false;
                }

                NotifyStatus($"Deleted shortcut '{shortcut.Name}'.");
                RefreshResults();
                return false;
            }),
            CreateContextMenu("Run as administrator", "\uEA18", _ =>
            {
                Launch(shortcut.Id, runAsAdmin: true);
                return true;
            }),
            CreateContextMenu("Open containing folder", "\uE838", _ => OpenContainingFolder(shortcut.Id)),
            CreateContextMenu("Copy path", ShortcutGlyphs.CopyPath, _ =>
            {
                var current = Shortcuts.GetStoredWorkspace(shortcut.Id);
                var authorization = current is null
                    ? null
                    : WorkspaceSecurityPolicy.Authorize(current, WorkspaceAction.CopyPath);
                if (authorization?.IsAllowed == true && !string.IsNullOrWhiteSpace(authorization.EffectiveValues.Directory))
                {
                    CopyPath(authorization.EffectiveValues.Directory);
                }
                return true;
            }),
        ]);
        return menus;
    }

    private IEnumerable<Result> GetManageResults(string search)
    {
        var utilities = new (RunManageAction Action, string Title, string Subtitle, int Score, string[] Keywords)[]
        {
            (RunManageAction.OpenQuickShellSettings, "Quick Shell settings", "Terminal defaults, git launch, recents, import/export", 2100, ["settings", "config", "preferences", "quickshell", "quick shell"]),
            (RunManageAction.CreateShortcut, "Create shortcut", "Add a new folder shortcut", 2000, ["new", "create", "add"]),
            (RunManageAction.ExportShortcuts, "Export shortcuts", "Save shortcuts to a JSON file", 1900, ["export", "backup"]),
            (RunManageAction.ImportMerge, "Import shortcuts (merge)", "Add shortcuts from a JSON file", 1850, ["import", "merge", "restore"]),
            (RunManageAction.ImportReplace, "Import shortcuts (replace all)", "Replace all shortcuts from a JSON file", 1840, ["replace"]),
            (RunManageAction.OpenShortcutsFile, "Open shortcuts.json", Shortcuts.ConfigPath, 1800, ["json", "shortcuts", "file"]),
            (RunManageAction.OpenSettingsFile, "Open settings.json", Settings.SettingsPath, 1750, ["settings", "config", "json"]),
        };

        var utilityOrder = 0;
        foreach (var utility in utilities)
        {
            if (!RunQueryScoring.ShouldIncludeUtility(search, utility.Keywords))
            {
                continue;
            }

            yield return new Result
            {
                Title = utility.Title,
                SubTitle = utility.Subtitle,
                IcoPath = _iconPath,
                Score = RunQueryScoring.ComputeUtilityScore(utility.Score, search, utilityOrder++),
                ContextData = new RunContextData(RunContextKind.Manage, ManageAction: utility.Action),
                Action = _ =>
                {
                    ExecuteManageAction(utility.Action);
                    return ShouldHideRunAfterManage(utility.Action);
                },
            };
        }
    }

    private void ExecuteManageAction(RunManageAction action)
    {
        switch (action)
        {
            case RunManageAction.OpenQuickShellSettings:
                QuickShellRunSettingsDialog.Show(Settings, Shortcuts, ProjectAnalysis, TerminalCatalog);
                _settingsPanel?.Reload();
                break;
            case RunManageAction.CreateShortcut:
                if (ShortcutEditor.TryShowDialog(
                        null,
                        Shortcuts,
                        EditorFactory,
                        ProjectAnalysis,
                        CommandSuggestions,
                        GitOperations,
                        TargetStore,
                        TerminalCatalog,
                        out var createMessage))
                {
                    NotifyStatus(createMessage);
                    RefreshResults();
                }

                break;
            case RunManageAction.ExportShortcuts:
                if (RunFileDialogs.TryExportShortcuts(Shortcuts, null, out var exportMessage))
                {
                    NotifyStatus(exportMessage);
                }

                break;
            case RunManageAction.ImportMerge:
                if (RunFileDialogs.TryImportShortcuts(Shortcuts, null, replace: false, out var mergeMessage)
                    && !string.IsNullOrWhiteSpace(mergeMessage))
                {
                    NotifyStatus(mergeMessage);
                    RefreshResults();
                }

                break;
            case RunManageAction.ImportReplace:
                if (RunFileDialogs.TryImportShortcuts(Shortcuts, null, replace: true, out var replaceMessage)
                    && !string.IsNullOrWhiteSpace(replaceMessage))
                {
                    NotifyStatus(replaceMessage);
                    RefreshResults();
                }

                break;
            case RunManageAction.OpenShortcutsFile:
                RunFileDialogs.OpenPathInEditor(Shortcuts.ConfigPath);
                break;
            case RunManageAction.OpenSettingsFile:
                RunFileDialogs.OpenPathInEditor(Settings.SettingsPath);
                break;
            default:
                throw new InvalidOperationException($"Unhandled manage action: {action}");
        }
    }

    private void ExecuteManageShortcutEdit(TerminalShortcut shortcut)
    {
        if (ShortcutEditor.TryShowDialog(shortcut, Shortcuts, EditorFactory, ProjectAnalysis, CommandSuggestions, GitOperations, TargetStore, TerminalCatalog, out var message))
        {
            NotifyStatus(message);
            RefreshResults();
        }
    }

    private static bool ShouldHideRunAfterManage(RunManageAction action) =>
        action is RunManageAction.OpenShortcutsFile
            or RunManageAction.OpenSettingsFile
            or RunManageAction.OpenQuickShellSettings;

    private void NotifyStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _context is null)
        {
            return;
        }

        _context.API.ShowNotification("Quick Shell", message);
    }

    private void RefreshResults()
    {
        if (_context is null || string.IsNullOrEmpty(_lastQuery))
        {
            return;
        }

        _context.API.ChangeQuery(_lastQuery, requery: true);
    }

    private Result CreateShortcutResult(TerminalShortcut shortcut, string search, bool directActivationBrowse)
    {
        var needsRepair = ShortcutHealth.WouldNeedRepair(shortcut);
        var trustPrefix = WorkspaceTrustFeatures.Enabled
            && Shortcuts.GetStoredWorkspace(shortcut.Id)?.Security.IsTrusted == false
                ? "Untrusted · "
                : string.Empty;
        var result = new Result
        {
            Title = shortcut.Name,
            SubTitle = trustPrefix + RunWorkspaceSubtitle.Build(shortcut, Settings, HealthChecker, GitOperations, TargetStore, TerminalCatalog, listMode: true),
            Score = RunQueryScoring.ComputeShortcutScore(shortcut, search, directActivationBrowse),
            ContextData = new RunContextData(RunContextKind.Shortcut, shortcut.Id),
            Action = action =>
            {
                if (needsRepair)
                {
                    ExecuteManageShortcutEdit(shortcut);
                    return false;
                }

                var forceAdmin = action.SpecialKeyState.CtrlPressed && action.SpecialKeyState.ShiftPressed;
                Launch(shortcut.Id, runAsAdmin: forceAdmin || shortcut.RunAsAdmin);
                return true;
            },
        };

        RunResultIcons.ApplyToResult(
            result,
            ShortcutHealth.GetListGlyph(shortcut, TerminalLaunchGlyphs),
            shortcut,
            TerminalLaunchGlyphs);
        return result;
    }

    private IEnumerable<TerminalShortcut> MergeSearchResults(string search)
    {
        var rootMatches = Shortcuts.SearchForRootPalette(search).ToArray();
        if (rootMatches.Length > 0)
        {
            return rootMatches;
        }

        return Shortcuts.Search(search);
    }

    private IWorkspaceHealthChecker HealthChecker =>
        _healthChecker ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IWorkspaceGitOperations GitOperations =>
        _gitOperations ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IWorktreeBranchTargetStore TargetStore =>
        _targetStore ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private ITerminalCatalog TerminalCatalog =>
        _terminalCatalog ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private ITerminalLaunchGlyphs TerminalLaunchGlyphs =>
        _terminalLaunchGlyphs ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IWorkspaceEditorFactory EditorFactory =>
        _editorFactory ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IProjectAnalysisService ProjectAnalysis =>
        _projectAnalysis ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private ICommandSuggestionService CommandSuggestions =>
        _commandSuggestions ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private void Launch(string workspaceId, bool runAsAdmin = false, bool runAsStandard = false)
    {
        var result = WorkspaceLaunch.Launch(
            workspaceId,
            Settings!.TerminalApplicationId,
            Settings.DefaultProfileId,
            new ShortcutLaunchOptions(
                runAsAdmin,
                runAsStandard,
                BlockDirtyBranchSwitch: Settings.ReadBlockDirtyBranchSwitch(),
                SeparateWindowsForMultiLaunch: Settings.ReadSeparateWindowsForMultiLaunch()));

        if (result.MarkUsed)
        {
            Shortcuts!.MarkUsed(workspaceId);
        }

        if (!result.Dismiss && !string.IsNullOrWhiteSpace(result.StayOpenMessage))
        {
            _context?.API.ShowMsg("Quick Shell", result.StayOpenMessage, string.Empty);
        }
    }

    private bool TrustWorkspace(StoredWorkspace storedWorkspace)
    {
        var review = Shortcuts.BeginTrustReview(storedWorkspace.Content.Id);
        if (review.Token is null || !review.Assessment.IsAllowed)
        {
            NotifyStatus("Repair this workspace before trusting it.");
            return false;
        }

        var risks = review.Assessment.Risks.Count == 0
            ? "No command, elevation, companion, or URL risks were detected."
            : string.Join(" ", review.Assessment.Risks.Select(risk => risk.Description));
        var confirmation = MessageBox.Show(
            "Trust applies to this editable local workspace. It can execute arbitrary code, and later command or launch-setting edits remain trusted until you revoke trust. " + risks,
            "Trust workspace?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return false;
        }

        var transition = Shortcuts.GrantTrust(storedWorkspace.Content.Id, review.Token);
        NotifyStatus(transition.Message);
        RefreshResults();
        return false;
    }

    private static bool IsActionKeywordQuery(Query query) =>
        !string.IsNullOrEmpty(query.ActionKeyword);

    private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

    private void UpdateIconPath(Theme theme)
    {
        _iconPath = theme is Theme.Light or Theme.HighContrastWhite
            ? "Images\\quickshell.light.png"
            : "Images\\quickshell.dark.png";
    }

    private static ContextMenuResult CreateContextMenu(string title, string glyph, Func<ActionContext, bool> action)
    {
        var (resolvedGlyph, fontFamily) = RunResultIcons.ResolveGlyph(glyph);
        return new ContextMenuResult
        {
            Title = title,
            Glyph = resolvedGlyph,
            FontFamily = fontFamily,
            Action = action,
        };
    }

    private bool OpenContainingFolder(string workspaceId)
    {
        var workspace = Shortcuts.GetStoredWorkspace(workspaceId);
        if (workspace is null)
        {
            return false;
        }

        var authorization = WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDirectory);
        var normalized = authorization.EffectiveValues.Directory;
        if (!authorization.IsAllowed || string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{normalized}\"",
            UseShellExecute = true,
        }) is not null;
    }

    private static bool CopyPath(string directory)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out _))
        {
            return false;
        }

        return StaClipboard.TrySetText(normalized);
    }
}
