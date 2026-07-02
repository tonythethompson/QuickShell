using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

internal sealed class FakeShortcutRepository : IShortcutRepository
{
    private readonly Dictionary<string, TerminalShortcut> _byId;
    private readonly Dictionary<string, TerminalShortcut> _byName;

    public FakeShortcutRepository(IEnumerable<TerminalShortcut> shortcuts, string? configDirectory = null)
    {
        var list = shortcuts.ToList();
        _byId = list.ToDictionary(shortcut => shortcut.Id, StringComparer.OrdinalIgnoreCase);
        _byName = list.ToDictionary(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase);
        ConfigDirectory = configDirectory ?? string.Empty;
    }

    public string ConfigDirectory { get; }

    public string ConfigPath => string.Empty;

    public IReadOnlyList<TerminalShortcut> GetShortcuts() => _byId.Values.ToList();

    public IReadOnlyList<ShortcutLayoutEntry> GetLayout() => [];

    public TerminalShortcut? GetByName(string name) =>
        _byName.TryGetValue(name, out var shortcut) ? shortcut : null;

    public TerminalShortcut? GetById(string id) =>
        _byId.TryGetValue(id, out var shortcut) ? shortcut : null;

    public TerminalShortcut? ResolveForOpenCommand(string key) => GetById(key) ?? GetByName(key);

    public void Reload()
    {
    }

    public void FlushPendingWrites()
    {
    }

    public bool TryExportToFile(string path, out string error)
    {
        error = string.Empty;
        return false;
    }

    public Task<ShortcutExportResult> TryExportToFileAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShortcutExportResult(false, string.Empty));

    public bool TryReadImportFile(string path, out TerminalShortcut[] shortcuts, out string error)
    {
        shortcuts = [];
        error = string.Empty;
        return false;
    }

    public Task<ShortcutImportReadResult> TryReadImportFileAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShortcutImportReadResult(false, [], string.Empty));

    public int CountImportNameConflicts(IReadOnlyList<TerminalShortcut> imported) => 0;

    public ShortcutTransferResult ImportMerge(string path) => new();

    public Task<ShortcutTransferResult> ImportMergeAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShortcutTransferResult());

    public ShortcutTransferResult ImportReplace(string path) => new();

    public Task<ShortcutTransferResult> ImportReplaceAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShortcutTransferResult());

    public ShortcutTransferResult ResetAll() => new() { Success = true, Message = "No workspaces to reset." };

    public bool CanUndo => false;

    public bool CanRedo => false;

    public bool Undo() => false;

    public bool Redo() => false;

    public void Upsert(TerminalShortcut shortcut, string? originalName = null)
    {
    }

    public bool Delete(string name) => false;

    public bool TogglePinned(string name) => false;

    public bool MovePinned(string name, int direction) => false;

    public bool MovePinnedToEdge(string name, bool toTop) => false;

    public void MarkUsed(string shortcutId)
    {
    }

    public TerminalShortcut? BuildDuplicate(string name) => null;

    public IEnumerable<TerminalShortcut> Search(string query) => GetShortcuts();

    public IEnumerable<TerminalShortcut> SearchForRootPalette(string query) => [];

    public string ResolveAvailableName(string desiredName, string? replacingOriginalName = null) => desiredName;
}
