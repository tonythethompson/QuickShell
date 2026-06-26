using QuickShell.Models;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Services;

internal interface IShortcutRepository
{
    string ConfigDirectory { get; }

    string ConfigPath { get; }

    IReadOnlyList<TerminalShortcut> GetShortcuts();

    TerminalShortcut? GetByName(string name);

    TerminalShortcut? GetById(string id);

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

    bool Undo();

    bool Redo();

    void Upsert(TerminalShortcut shortcut, string? originalName = null);

    bool Delete(string name);

    bool TogglePinned(string name);

    bool MovePinned(string name, int direction);

    void MarkUsed(string shortcutId);

    TerminalShortcut? BuildDuplicate(string name);

    IEnumerable<TerminalShortcut> Search(string query);

    IEnumerable<TerminalShortcut> SearchForRootPalette(string query);

    string ResolveAvailableName(string desiredName, string? replacingOriginalName = null);
}
