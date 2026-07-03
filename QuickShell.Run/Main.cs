using System.IO;

using System.Reflection;

using System.Runtime.Loader;

using System.Windows.Controls;

using ManagedCommon;

using Microsoft.PowerToys.Settings.UI.Library;

using QuickShell.Models;

using QuickShell.Services;

using Wox.Plugin;



namespace QuickShell.Run;



public class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider, IReloadable, IDisposable

{

    public const string PluginIdValue = "a7c3e891-4b2d-4f6e-9c1a-2d8e5f03b4c6";



    public static string PluginID => PluginIdValue;



    private const string IconFont = "Segoe MDL2 Assets";



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

    private ShortcutRepository? _shortcuts;

    private QuickShellSettingsReader? _settings;

    private QuickShellRunSettingsPanel? _settingsPanel;

    private string _lastQuery = string.Empty;

    private bool _disposed;



    public string Name => "Quick Shell";



    public string Description => "Open saved folders in any terminal you use";



    public string GetTranslatedPluginTitle() => Name;



    public string GetTranslatedPluginDescription() => Description;



    public void Init(PluginInitContext context)

    {

        _shortcuts = new ShortcutRepository();

        _settings = new QuickShellSettingsReader();

        _context = context;

        UpdateIconPath(context.API.GetCurrentTheme());

        context.API.ThemeChanged += OnThemeChanged;

        _shortcuts.Reload();

    }



    public void Dispose()

    {

        if (_disposed)

        {

            return;

        }



        if (_context?.API is not null)

        {

            _context.API.ThemeChanged -= OnThemeChanged;

        }



        _shortcuts?.Dispose();

        _disposed = true;

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

            (_, _) => { });

        _settingsPanel.Reload();

        return _settingsPanel;

    }



    public IEnumerable<PluginAdditionalOption> AdditionalOptions => [];



    public void UpdateSettings(PowerLauncherPluginSettings settings) =>

        _settingsPanel?.UpdateSettings(settings);



    private ShortcutRepository Shortcuts =>

        _shortcuts ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");



    private QuickShellSettingsReader Settings =>

        _settings ?? throw new InvalidOperationException("Quick Shell plugin is not initialized.");



    public List<Result> Query(Query query)

    {

        _lastQuery = query.RawQuery;

        var search = query.Search?.Trim() ?? string.Empty;



        if (ShouldSuppressGlobalQuery(query, search))

        {

            return [];

        }



        var results = new List<Result>();



        if (IsActionKeywordQuery(query))

        {

            results.AddRange(GetManageResults(search));

        }



        var shortcuts = string.IsNullOrWhiteSpace(search)

            ? Shortcuts.GetShortcuts()

            : MergeSearchResults(search);



        results.AddRange(shortcuts

            .Select(shortcut => CreateShortcutResult(shortcut, search))

            .OrderByDescending(result => result.Score)

            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase));



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



        if (ShortcutHealth.NeedsRepair(shortcut))

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

            CreateContextMenu("Duplicate shortcut", "\uE8C8", _ =>

            {

                var duplicate = Shortcuts.BuildDuplicate(shortcut.Name);

                if (duplicate is null)

                {

                    return false;

                }



                if (ShortcutEditor.TryShowDialog(duplicate, Shortcuts, out var message))

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

            CreateContextMenu("Copy path", "\uE8C8", _ =>

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

            (RunManageAction.CreateShortcut, "Create shortcut", "Add a new folder shortcut", 2000, ["new", "create", "add"]),

            (RunManageAction.ExportShortcuts, "Export shortcuts", "Save shortcuts to a JSON file", 1900, ["export", "backup"]),

            (RunManageAction.ImportMerge, "Import shortcuts (merge)", "Add shortcuts from a JSON file", 1850, ["import", "merge", "restore"]),

            (RunManageAction.ImportReplace, "Import shortcuts (replace all)", "Replace all shortcuts from a JSON file", 1840, ["replace"]),

            (RunManageAction.OpenShortcutsFile, "Open shortcuts.json", Shortcuts.ConfigPath, 1800, ["json", "shortcuts", "file"]),

            (RunManageAction.OpenSettingsFile, "Open Quick Shell settings", Settings.SettingsPath, 1750, ["settings", "config"]),

        };



        foreach (var utility in utilities)

        {

            if (!ShouldIncludeUtility(search, utility.Keywords))

            {

                continue;

            }



            yield return new Result

            {

                Title = utility.Title,

                SubTitle = utility.Subtitle,

                IcoPath = _iconPath,

                Score = utility.Score,

                ContextData = new RunContextData(RunContextKind.Manage, ManageAction: utility.Action),

                Action = _ =>

                {

                    ExecuteManageAction(utility.Action);

                    return ShouldHideRunAfterManage(utility.Action);

                },

            };

        }

    }



    private static bool ShouldIncludeUtility(string search, string[] keywords)

    {

        if (string.IsNullOrWhiteSpace(search))

        {

            return true;

        }



        return keywords.Any(keyword => keyword.Contains(search, StringComparison.OrdinalIgnoreCase)

            || search.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    }



    private void ExecuteManageAction(RunManageAction action)

    {

        switch (action)

        {

            case RunManageAction.CreateShortcut:

                if (ShortcutEditor.TryShowDialog(null, Shortcuts, out var createMessage))

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

        }

    }



    private void ExecuteManageShortcutEdit(TerminalShortcut shortcut)

    {

        if (ShortcutEditor.TryShowDialog(shortcut, Shortcuts, out var message))

        {

            NotifyStatus(message);

            RefreshResults();

        }

    }



    private static bool ShouldHideRunAfterManage(RunManageAction action) =>

        action is RunManageAction.OpenShortcutsFile or RunManageAction.OpenSettingsFile;



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



    private Result CreateShortcutResult(TerminalShortcut shortcut, string search)

    {

        var needsRepair = ShortcutHealth.NeedsRepair(shortcut);

        return new Result

        {

            Title = shortcut.Name,

            SubTitle = ShortcutHealth.BuildListSubtitle(shortcut),

            Glyph = ShortcutHealth.GetListGlyph(shortcut),

            FontFamily = IconFont,

            Score = ComputeScore(shortcut, search),

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



    private static int ComputeScore(TerminalShortcut shortcut, string search)

    {

        var score = shortcut.IsPinned ? 100 : 0;



        if (!string.IsNullOrWhiteSpace(search)

            && !string.IsNullOrWhiteSpace(shortcut.Abbreviation)

            && shortcut.Abbreviation.Equals(search, StringComparison.OrdinalIgnoreCase))

        {

            score += 200;

        }

        else if (!string.IsNullOrWhiteSpace(search)

            && !string.IsNullOrWhiteSpace(shortcut.Abbreviation)

            && shortcut.Abbreviation.StartsWith(search, StringComparison.OrdinalIgnoreCase))

        {

            score += 120;

        }



        if (shortcut.LastUsedUtc is not null)

        {

            var ageHours = Math.Max(0, (DateTime.UtcNow - shortcut.LastUsedUtc.Value).TotalHours);

            score += (int)Math.Max(0, 40 - ageHours);

        }



        return score;

    }



    private void Launch(TerminalShortcut shortcut, bool runAsAdmin = false, bool runAsStandard = false)
    {
        var result = ShortcutLaunchExecutor.Launch(
            shortcut,
            Settings!.TerminalApplicationId,
            Settings.DefaultProfileId,
            new ShortcutLaunchOptions(runAsAdmin, runAsStandard));

        if (result.MarkUsed)
        {
            Shortcuts!.MarkUsed(shortcut.Id);
        }

        if (!result.Dismiss && !string.IsNullOrWhiteSpace(result.StayOpenMessage))
        {
            _context?.API.ShowMsg("Quick Shell", result.StayOpenMessage, string.Empty);
        }
    }



    private static bool ShouldSuppressGlobalQuery(Query query, string search)

    {

        if (!string.IsNullOrEmpty(query.ActionKeyword))

        {

            return false;

        }



        if (string.IsNullOrWhiteSpace(search))

        {

            return true;

        }



        return search.Contains("quick shell", StringComparison.OrdinalIgnoreCase);

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



    private static ContextMenuResult CreateContextMenu(string title, string glyph, Func<ActionContext, bool> action) =>

        new()

        {

            Title = title,

            Glyph = glyph,

            FontFamily = IconFont,

            Action = action,

        };



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


