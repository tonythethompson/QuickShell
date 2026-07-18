using QuickShell.Models;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Services;

internal interface IShortcutRepository
{
    event EventHandler? WorkspacesChanged;

    string ConfigDirectory { get; }

    string ConfigPath { get; }

    Task PreloadAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<TerminalShortcut> GetShortcuts();

    IReadOnlyList<ShortcutLayoutEntry> GetLayout();

    WorkspaceRepositorySnapshot GetSnapshot();

    TerminalShortcut? GetByName(string name);

    TerminalShortcut? GetById(string id);

    TerminalShortcut? GetByNameReadOnly(string name);

    TerminalShortcut? GetByIdReadOnly(string id);

    TerminalShortcut? ResolveForOpenCommand(string key);

    void Reload();

    void FlushPendingWrites();

    bool TryExportToFile(string path, out string error);

    Task<ShortcutExportResult> TryExportToFileAsync(string path, CancellationToken cancellationToken = default);

    bool TryReadImportFile(string path, out TerminalShortcut[] shortcuts, out string error);

    Task<ShortcutImportReadResult> TryReadImportFileAsync(string path, CancellationToken cancellationToken = default);

    int CountImportNameConflicts(IReadOnlyList<TerminalShortcut> imported);

    ShortcutTransferResult ImportMerge(string path);

    Task<ShortcutTransferResult> ImportMergeAsync(string path, CancellationToken cancellationToken = default);

    ShortcutTransferResult ImportReplace(string path);

    Task<ShortcutTransferResult> ImportReplaceAsync(string path, CancellationToken cancellationToken = default);

    ShortcutTransferResult ResetAll();

    bool CanUndo { get; }

    bool CanRedo { get; }

    bool Undo();

    bool Redo();

    void Upsert(TerminalShortcut shortcut, string? originalName = null);

    bool Delete(string name);

    bool TogglePinned(string name);

    bool MovePinned(string name, int direction);

    bool MovePinnedToEdge(string name, bool toTop);

    /// <summary>Reorder favorites by stable workspace id (preferred over name).</summary>
    bool MovePinnedById(string id, int direction);

    bool MovePinnedToEdgeById(string id, bool toTop);

    void MarkUsed(string shortcutId);

    TerminalShortcut? BuildDuplicate(string name);

    TerminalShortcut BuildDuplicateFrom(TerminalShortcut source);

    IEnumerable<TerminalShortcut> Search(string query);

    IEnumerable<TerminalShortcut> SearchForRootPalette(string query);

    IEnumerable<WorkspaceTaskAction> SearchTaskActions(string query);

    string ResolveAvailableName(string desiredName, string? replacingOriginalName = null);
}
