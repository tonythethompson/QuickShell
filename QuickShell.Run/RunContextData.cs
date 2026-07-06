namespace QuickShell.Run;

internal enum RunManageAction
{
    OpenQuickShellSettings,
    CreateShortcut,
    ExportShortcuts,
    ImportMerge,
    ImportReplace,
    OpenShortcutsFile,
    OpenSettingsFile,
}

internal enum RunContextKind
{
    Shortcut,
    Manage,
}

internal readonly record struct RunContextData(RunContextKind Kind, string? ShortcutId = null, RunManageAction? ManageAction = null);
