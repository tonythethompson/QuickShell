using QuickShell.Models;
using QuickShell.Services;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Core.Tests;

internal sealed class FakeShortcutRepository : IShortcutRepository
{
    // Interface requires the event; this fake never raises it.
    public event EventHandler? WorkspacesChanged
    {
        add { }
        remove { }
    }

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

    public long Version { get; set; }

    // Mirrors the real repository's structural-vs-usage version split (see
    // WorkspaceRepositorySnapshot.StructuralVersion). BumpVersion() bumps both, matching
    // existing tests that use it to represent structural changes (edit/delete/reorder);
    // BumpUsageOnlyVersion() bumps only Version, simulating a MarkUsed-only update.
    public long StructuralVersion { get; set; }

    public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IReadOnlyList<TerminalShortcut> GetShortcuts() => _byId.Values.ToList();

    public IReadOnlyList<ShortcutLayoutEntry> GetLayout() => [];

    public WorkspaceRepositorySnapshot GetSnapshot() =>
        new(Version, GetShortcuts(), GetLayout(), StructuralVersion: StructuralVersion);

    public void BumpVersion()
    {
        Version++;
        StructuralVersion++;
    }

    public void BumpUsageOnlyVersion() => Version++;

    public void Clear()
    {
        _byId.Clear();
        _byName.Clear();
    }

    public TerminalShortcut? GetByName(string name) =>
        _byName.TryGetValue(name, out var shortcut) ? shortcut : null;

    public TerminalShortcut? GetById(string id) =>
        _byId.TryGetValue(id, out var shortcut) ? ShortcutRepository.Clone(shortcut) : null;

    public TerminalShortcut? GetByNameReadOnly(string name) => GetByName(name);

    public TerminalShortcut? GetByIdReadOnly(string id) => GetById(id);

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
        if (!string.IsNullOrWhiteSpace(originalName)
            && _byName.TryGetValue(originalName, out var existing))
        {
            _byId.Remove(existing.Id);
            _byName.Remove(originalName);
        }

        _byId[shortcut.Id] = shortcut;
        _byName[shortcut.Name] = shortcut;
    }

    public bool Delete(string name) => false;

    public bool TogglePinned(string name) => false;

    public bool MovePinned(string name, int direction) => false;

    public bool MovePinnedToEdge(string name, bool toTop) => false;

    public bool MovePinnedById(string id, int direction) => false;

    public bool MovePinnedToEdgeById(string id, bool toTop) => false;

    public void MarkUsed(string shortcutId)
    {
    }

    public TerminalShortcut? BuildDuplicate(string name) => null;

    public TerminalShortcut BuildDuplicateFrom(TerminalShortcut source) => source;

    public IEnumerable<TerminalShortcut> Search(string query) => GetShortcuts();

    public IEnumerable<TerminalShortcut> SearchForRootPalette(string query) => [];

    public IEnumerable<WorkspaceTaskAction> SearchTaskActions(string query) => [];

    public string ResolveAvailableName(string desiredName, string? replacingOriginalName = null) => desiredName;
}
