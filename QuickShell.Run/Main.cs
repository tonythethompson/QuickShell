using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Controls;
using ManagedCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerToys.Settings.UI.Library;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Models;
using QuickShell.Services;
using Wox.Plugin;

namespace QuickShell.Run;

public class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable
{
    public const string PluginIdValue = "a7c3e891-4b2d-4f6e-9c1a-2d8e5f03b4c6";

    public static string PluginID => PluginIdValue;

    static Main()
    {
        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(pluginDir))
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            var candidate = Path.Combine(pluginDir, $"{assemblyName.Name}.dll");
            return File.Exists(candidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        };
    }

    private PluginInitContext? _context;
    private string _iconPath = string.Empty;
    private ServiceProvider? _serviceProvider;
    private IQuickShellLifetime? _lifetime;
    private IShortcutRepository? _shortcuts;
    private QuickShellSettingsReader? _settings;
    private QuickShellRunSettingsPanel? _settingsPanel;
    private IProjectAnalysisService? _projectAnalysis;
    private ICommandSuggestionService? _commandSuggestions;
    private IShortcutLaunchExecutor? _launchExecutor;
    private IWorkspaceHealthChecker? _healthChecker;
    private IWorkspaceGitOperations? _gitOperations;
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
            _projectAnalysis = _serviceProvider.GetRequiredService<IProjectAnalysisService>();
            _commandSuggestions = _serviceProvider.GetRequiredService<ICommandSuggestionService>();
            _launchExecutor = _serviceProvider.GetRequiredService<IShortcutLaunchExecutor>();
            _healthChecker = _serviceProvider.GetRequiredService<IWorkspaceHealthChecker>();
            _gitOperations = _serviceProvider.GetRequiredService<IWorkspaceGitOperations>();
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
            _projectAnalysis ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."),
            _commandSuggestions ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."),
            GitOperations,
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

        var shortcuts = string.IsNullOrWhiteSpace(filter)
            ? Shortcuts.GetShortcuts()
            : MergeSearchResults(filter);

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

        return
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

                if (ShortcutEditor.TryShowDialog(duplicate, Shortcuts, _projectAnalysis ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."), _commandSuggestions ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."), GitOperations, out var message))
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
                Launch(shortcut, runAsAdmin: true);
                return true;
            }),
            CreateContextMenu("Open containing folder", "\uE838", _ => OpenContainingFolder(shortcut.Directory)),
            CreateContextMenu("Copy path", ShortcutGlyphs.CopyPath, _ =>
            {
                CopyPath(shortcut.Directory);
                return true;
            }),
        ];
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
                QuickShellRunSettingsDialog.Show(Settings, Shortcuts, _projectAnalysis ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."));
                _settingsPanel?.Reload();
                break;
            case RunManageAction.CreateShortcut:
                if (ShortcutEditor.TryShowDialog(
                        null,
                        Shortcuts,
                        _projectAnalysis ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."),
                        _commandSuggestions ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."),
                        GitOperations,
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
        if (ShortcutEditor.TryShowDialog(shortcut, Shortcuts, _projectAnalysis ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."), _commandSuggestions ?? throw new InvalidOperationException("Quick Shell plugin is not initialized."), GitOperations, out var message))
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
        var result = new Result
        {
            Title = shortcut.Name,
            SubTitle = RunWorkspaceSubtitle.Build(
                shortcut,
                Settings,
                HealthChecker,
                GitOperations,
                listMode: true),
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
                Launch(shortcut, runAsAdmin: forceAdmin || shortcut.RunAsAdmin);
                return true;
            },
        };

        RunResultIcons.ApplyToResult(result, ShortcutHealth.GetListGlyph(shortcut), shortcut);
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

    private IShortcutLaunchExecutor LaunchExecutor =>
        _launchExecutor ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IWorkspaceHealthChecker HealthChecker =>
        _healthChecker ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private IWorkspaceGitOperations GitOperations =>
        _gitOperations ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");

    private void Launch(TerminalShortcut shortcut, bool runAsAdmin = false, bool runAsStandard = false)
    {
        var result = LaunchExecutor.Launch(
            shortcut,
            Settings!.TerminalApplicationId,
            Settings.DefaultProfileId,
            new ShortcutLaunchOptions(
                runAsAdmin,
                runAsStandard,
                BlockDirtyBranchSwitch: Settings.ReadBlockDirtyBranchSwitch(),
                SeparateWindowsForMultiLaunch: Settings.ReadSeparateWindowsForMultiLaunch()));

        if (result.MarkUsed)
        {
            Shortcuts!.MarkUsed(shortcut.Id);
        }

        if (!result.Dismiss && !string.IsNullOrWhiteSpace(result.StayOpenMessage))
        {
            _context?.API.ShowMsg("Quick Shell", result.StayOpenMessage, string.Empty);
        }
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

    private static bool OpenContainingFolder(string directory)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out _))
        {
            return false;
        }

        if (!ShortcutValidation.DirectoryExists(normalized))
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
