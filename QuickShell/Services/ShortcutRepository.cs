using QuickShell.Models;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuickShell.Services;

internal sealed class ShortcutTransferResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int Imported { get; init; }

    public int Skipped { get; init; }

    public int Renamed { get; init; }
}

internal readonly record struct ShortcutExportResult(bool Success, string Error);

internal readonly record struct ShortcutImportReadResult(bool Success, TerminalShortcut[] Shortcuts, string Error);

internal sealed partial class ShortcutRepository : IShortcutRepository, IDisposable
{
    private const int MaxConfigBytes = 2 * 1024 * 1024;
    private const int MaxHistoryEntries = 50;

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Mutex _fileMutex = new(false, @"Global\QuickShell_shortcuts_json");

    private TerminalShortcut[] _shortcuts = [];
    private List<ShortcutLayoutEntry> _layout = [];
    private List<ShortcutLayoutEntry> _lastGoodLayout = [];
    private readonly List<List<ShortcutLayoutEntry>> _undoHistory = [];
    private readonly List<List<ShortcutLayoutEntry>> _redoHistory = [];
    private DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private bool _configEnsured;
    private bool _persistPending;
    private Timer? _persistTimer;
    private bool _disposed;

    public string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickShell");

    public string ConfigPath => Path.Combine(ConfigDirectory, "shortcuts.json");

    public IReadOnlyList<TerminalShortcut> GetShortcuts() =>
        WithLock(() =>
        {
            EnsureLoaded();
            return CloneAll(_shortcuts);
        });

    public IReadOnlyList<ShortcutLayoutEntry> GetLayout() =>
        WithLock(() =>
        {
            EnsureLoaded();
            return CloneLayout(_layout);
        });

    public TerminalShortcut? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            var shortcut = _shortcuts.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return shortcut is null ? null : Clone(shortcut);
        });
    }

    public TerminalShortcut? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            var shortcut = _shortcuts.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return shortcut is null ? null : Clone(shortcut);
        });
    }

    public TerminalShortcut? ResolveForOpenCommand(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var byId = GetById(key);
        if (byId is not null)
        {
            return byId;
        }

        if (ShortcutCommandIds.TryDecodeLegacyNameKey(key, out var legacyName))
        {
            return GetByName(legacyName);
        }

        return null;
    }

    public void Reload() =>
        WithLock(() =>
        {
            CancelPendingPersist();
            _lastWriteTimeUtc = DateTime.MinValue;
            EnsureLoaded(force: true);
        });

    public void FlushPendingWrites() =>
        WithLock(FlushPendingPersistLocked);

    public bool TryExportToFile(string path, out string error)
    {
        var result = TryExportToFileAsync(path).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public async Task<ShortcutExportResult> TryExportToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ShortcutExportResult(false, "Export path is required.");
        }

        byte[] payload;

        try
        {
            var prepare = WithLock(() =>
            {
                EnsureLoaded();
                FlushPendingPersistLocked();

                var payload = ShortcutLayoutJson.Serialize(_layout);
                if (payload.Length > MaxConfigBytes)
                {
                    return (Success: false, Payload: Array.Empty<byte>());
                }

                return (Success: true, Payload: payload);
            });

            if (!prepare.Success)
            {
                return new ShortcutExportResult(false, "Shortcut data is too large to export.");
            }

            payload = prepare.Payload;

            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, payload, cancellationToken).ConfigureAwait(false);
            return new ShortcutExportResult(true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return new ShortcutExportResult(false, "Export cancelled.");
        }
        catch (IOException)
        {
            return new ShortcutExportResult(false, "Export failed: unable to write the destination file.");
        }
        catch (UnauthorizedAccessException)
        {
            return new ShortcutExportResult(false, "Export failed: access to the destination path was denied.");
        }
        catch (ArgumentException)
        {
            return new ShortcutExportResult(false, "Export failed: the destination path is invalid.");
        }
        catch (NotSupportedException)
        {
            return new ShortcutExportResult(false, "Export failed: the destination path format is not supported.");
        }
    }

    public bool TryReadImportFile(string path, out TerminalShortcut[] shortcuts, out string error)
    {
        var result = TryReadImportFileAsync(path).GetAwaiter().GetResult();
        shortcuts = result.Shortcuts;
        error = result.Error;
        return result.Success;
    }

    public async Task<ShortcutImportReadResult> TryReadImportFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ShortcutImportReadResult(false, [], "Import path is required.");
        }

        if (!File.Exists(path))
        {
            return new ShortcutImportReadResult(false, [], "File not found.");
        }

        try
        {
            var (loaded, layout) = await TryLoadLayoutFromFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (!loaded || CountValidShortcuts(layout) == 0)
            {
                return new ShortcutImportReadResult(false, [], "No valid shortcuts were found in that file.");
            }

            return new ShortcutImportReadResult(true, ShortcutLayoutJson.ExtractShortcuts(layout), string.Empty);
        }
        catch (OperationCanceledException)
        {
            return new ShortcutImportReadResult(false, [], "Import cancelled.");
        }
    }

    public int CountImportNameConflicts(IReadOnlyList<TerminalShortcut> imported)
    {
        if (imported.Count == 0)
        {
            return 0;
        }

        var existingNames = GetShortcuts()
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return imported.Count(shortcut => existingNames.Contains(shortcut.Name));
    }

    public ShortcutTransferResult ImportMerge(string path)
    {
        if (!TryReadImportFile(path, out var imported, out var error))
        {
            return new ShortcutTransferResult
            {
                Success = false,
                Message = error,
            };
        }

        return ImportMergeCore(imported);
    }

    public async Task<ShortcutTransferResult> ImportMergeAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var readResult = await TryReadImportFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = readResult.Error,
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ImportMergeCore(readResult.Shortcuts);
        }
        catch (OperationCanceledException)
        {
            return new ShortcutTransferResult
            {
                Success = false,
                Message = "Import cancelled.",
            };
        }
    }

    public ShortcutTransferResult ImportReplace(string path)
    {
        if (!TryReadImportLayout(path, out var layout, out var error))
        {
            return new ShortcutTransferResult
            {
                Success = false,
                Message = error,
            };
        }

        return ImportReplaceCore(layout);
    }

    public async Task<ShortcutTransferResult> ImportReplaceAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var readResult = await TryReadImportLayoutAsync(path, cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = readResult.Error,
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ImportReplaceCore(readResult.Layout);
        }
        catch (OperationCanceledException)
        {
            return new ShortcutTransferResult
            {
                Success = false,
                Message = "Import cancelled.",
            };
        }
    }

    private static bool TryReadImportLayout(string path, out List<ShortcutLayoutEntry> layout, out string error)
    {
        var result = TryReadImportLayoutAsync(path).GetAwaiter().GetResult();
        layout = result.Layout;
        error = result.Error;
        return result.Success;
    }

    private static async Task<(bool Success, List<ShortcutLayoutEntry> Layout, string Error)> TryReadImportLayoutAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (false, [], "Import path is required.");
        }

        if (!File.Exists(path))
        {
            return (false, [], "File not found.");
        }

        try
        {
            var (loaded, layout) = await TryLoadLayoutFromFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (!loaded || CountValidShortcuts(layout) == 0)
            {
                return (false, [], "No valid shortcuts were found in that file.");
            }

            return (true, layout, string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private ShortcutTransferResult ImportMergeCore(TerminalShortcut[] imported) =>
        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var existingNames = ShortcutLayoutJson.ExtractShortcuts(layout)
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedCount = 0;
            var skipped = 0;
            var renamed = 0;

            foreach (var source in imported)
            {
                var shortcut = Clone(source);
                shortcut.LastUsedUtc = null;

                if (!ShortcutValidation.TryValidateForImport(shortcut, out _))
                {
                    skipped++;
                    continue;
                }

                var uniqueName = GetUniqueName(shortcut.Name, existingNames);
                if (!uniqueName.Equals(shortcut.Name, StringComparison.Ordinal))
                {
                    renamed++;
                    shortcut.Name = uniqueName;
                }

                existingNames.Add(shortcut.Name);
                AssignShortcutId(shortcut, ShortcutLayoutJson.ExtractShortcuts(layout));

                if (shortcut.IsPinned && shortcut.PinOrder is null)
                {
                    shortcut.PinOrder = NextPinOrder(ShortcutLayoutJson.ExtractShortcuts(layout));
                }

                layout.Add(ShortcutLayoutEntry.FromShortcut(shortcut));
                importedCount++;
            }

            if (importedCount == 0)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = "No shortcuts could be imported from that file.",
                    Skipped = skipped,
                };
            }

            if (CountValidShortcuts(layout) > ShortcutValidation.MaxShortcutCount)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = $"Import would exceed the {ShortcutValidation.MaxShortcutCount}-shortcut limit.",
                };
            }

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);

            return new ShortcutTransferResult
            {
                Success = true,
                Message = BuildImportMessage(importedCount, skipped, renamed),
                Imported = importedCount,
                Skipped = skipped,
                Renamed = renamed,
            };
        });

    private ShortcutTransferResult ImportReplaceCore(List<ShortcutLayoutEntry> importedLayout) =>
        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(importedLayout);
            var valid = new List<ShortcutLayoutEntry>();
            var skipped = 0;

            foreach (var entry in layout)
            {
                if (entry.Kind == ShortcutLayoutEntryKind.Separator)
                {
                    valid.Add(ShortcutLayoutEntry.FromSeparator(entry.SeparatorTitle));
                    continue;
                }

                if (entry.Shortcut is null)
                {
                    skipped++;
                    continue;
                }

                var shortcut = Clone(entry.Shortcut);
                shortcut.LastUsedUtc = null;

                if (!ShortcutValidation.TryValidateForImport(shortcut, out _))
                {
                    skipped++;
                    continue;
                }

                valid.Add(ShortcutLayoutEntry.FromShortcut(shortcut));
            }

            if (CountValidShortcuts(valid) == 0)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = "No shortcuts could be imported from that file.",
                    Skipped = skipped,
                };
            }

            if (CountValidShortcuts(valid) > ShortcutValidation.MaxShortcutCount)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = $"Import exceeds the {ShortcutValidation.MaxShortcutCount}-shortcut limit.",
                };
            }

            NormalizeLayout(valid);
            RecordHistoryLayoutLocked(previous, valid);
            SaveLayoutLocked(valid);

            return new ShortcutTransferResult
            {
                Success = true,
                Message = BuildImportMessage(CountValidShortcuts(valid), skipped, renamed: 0),
                Imported = CountValidShortcuts(valid),
                Skipped = skipped,
            };
        });

    public bool CanUndo =>
        WithLock(() =>
        {
            EnsureLoaded();
            return _undoHistory.Count > 0;
        });

    public bool CanRedo =>
        WithLock(() =>
        {
            EnsureLoaded();
            return _redoHistory.Count > 0;
        });

    public bool Undo() =>
        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();

            if (_undoHistory.Count == 0)
            {
                return false;
            }

            var current = CloneLayout(_layout);
            var previous = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            PushLayoutHistory(_redoHistory, current);
            SaveLayoutLocked(previous);
            return true;
        });

    public bool Redo() =>
        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();

            if (_redoHistory.Count == 0)
            {
                return false;
            }

            var current = CloneLayout(_layout);
            var next = _redoHistory[^1];
            _redoHistory.RemoveAt(_redoHistory.Count - 1);
            PushLayoutHistory(_undoHistory, current);
            SaveLayoutLocked(next);
            return true;
        });

    public void Upsert(TerminalShortcut shortcut, string? originalName = null)
    {
        if (!ShortcutValidation.TryValidate(shortcut, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        if (!ShortcutValidation.TryValidateUniqueName(shortcut.Name, originalName, out validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var cloned = Clone(shortcut);

            var existingEntry = FindShortcutEntry(layout, cloned.Name)
                ?? (string.IsNullOrWhiteSpace(originalName) ? null : FindShortcutEntry(layout, originalName));

            if (existingEntry?.Shortcut is not null)
            {
                cloned.Id = existingEntry.Shortcut.Id;
                cloned.IsPinned = existingEntry.Shortcut.IsPinned;
                cloned.PinOrder = existingEntry.Shortcut.PinOrder;
                cloned.LastUsedUtc = existingEntry.Shortcut.LastUsedUtc;
            }
            else
            {
                AssignShortcutId(cloned, ShortcutLayoutJson.ExtractShortcuts(layout));
            }

            if (cloned.IsPinned && cloned.PinOrder is null)
            {
                cloned.PinOrder = NextPinOrder(ShortcutLayoutJson.ExtractShortcuts(layout));
            }

            if (existingEntry is not null)
            {
                existingEntry.Shortcut = cloned;
            }
            else
            {
                layout.Add(ShortcutLayoutEntry.FromShortcut(cloned));
            }

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);
        });
    }

    public bool Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var removed = RemoveShortcutEntry(layout, name);
            if (removed)
            {
                RecordHistoryLayoutLocked(previous, layout);
                SaveLayoutLocked(layout);
            }

            return removed;
        });
    }

    public bool TogglePinned(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var entry = FindShortcutEntry(layout, name);
            if (entry?.Shortcut is null)
            {
                return false;
            }

            entry.Shortcut.IsPinned = !entry.Shortcut.IsPinned;
            entry.Shortcut.PinOrder = entry.Shortcut.IsPinned
                ? NextPinOrder(ShortcutLayoutJson.ExtractShortcuts(layout))
                : null;
            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);
            return entry.Shortcut.IsPinned;
        });
    }

    public bool MovePinned(string name, int direction)
    {
        if (string.IsNullOrWhiteSpace(name) || direction == 0)
        {
            return false;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var shortcuts = ShortcutLayoutJson.ExtractShortcuts(layout);
            var pinned = shortcuts
                .Where(s => s.IsPinned)
                .OrderBy(s => s.PinOrder ?? int.MaxValue)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var index = pinned.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            var target = index + direction;
            if (target < 0 || target >= pinned.Count)
            {
                return false;
            }

            (pinned[index], pinned[target]) = (pinned[target], pinned[index]);
            for (var i = 0; i < pinned.Count; i++)
            {
                SetPinOrder(shortcuts, pinned[i].Name, i + 1);
            }

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);
            return true;
        });
    }

    public bool MovePinnedToEdge(string name, bool toTop)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var shortcuts = ShortcutLayoutJson.ExtractShortcuts(layout);
            var pinned = shortcuts
                .Where(s => s.IsPinned)
                .OrderBy(s => s.PinOrder ?? int.MaxValue)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var index = pinned.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            var target = toTop ? 0 : pinned.Count - 1;
            if (index == target)
            {
                return false;
            }

            var item = pinned[index];
            pinned.RemoveAt(index);
            pinned.Insert(target, item);

            for (var i = 0; i < pinned.Count; i++)
            {
                SetPinOrder(shortcuts, pinned[i].Name, i + 1);
            }

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);
            return true;
        });
    }

    public void MarkUsed(string shortcutId)
    {
        if (string.IsNullOrWhiteSpace(shortcutId))
        {
            return;
        }

        WithLock(() =>
        {
            EnsureLoaded();

            var entry = _layout.FirstOrDefault(item =>
                item.Kind == ShortcutLayoutEntryKind.Shortcut &&
                item.Shortcut is not null &&
                item.Shortcut.Id.Equals(shortcutId, StringComparison.OrdinalIgnoreCase));
            if (entry?.Shortcut is null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (entry.Shortcut.LastUsedUtc is not null && (now - entry.Shortcut.LastUsedUtc.Value).TotalSeconds < 2)
            {
                return;
            }

            entry.Shortcut.LastUsedUtc = now;
            SyncShortcutsFromLayout(_layout);
            SchedulePersistLocked();
        });
    }

    public TerminalShortcut? BuildDuplicate(string name)
    {
        var source = GetByName(name);
        if (source is null)
        {
            return null;
        }

        var copy = Clone(source);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = GetDuplicateName(copy.Name);
        copy.IsPinned = false;
        copy.PinOrder = null;
        copy.LastUsedUtc = null;
        return copy;
    }

    public IEnumerable<TerminalShortcut> Search(string query)
    {
        var shortcuts = GetShortcuts();
        if (string.IsNullOrWhiteSpace(query))
        {
            return shortcuts;
        }

        return shortcuts.Where(shortcut => Matches(shortcut, query.Trim()));
    }

    public IEnumerable<TerminalShortcut> SearchForRootPalette(string query)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        var shortcuts = GetShortcuts();
        var abbreviationMatches = shortcuts
            .Where(shortcut => !string.IsNullOrWhiteSpace(shortcut.Abbreviation)
                && shortcut.Abbreviation.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(shortcut => shortcut.Abbreviation!.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(shortcut => shortcut.Abbreviation!.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(shortcut => shortcut.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (abbreviationMatches.Length > 0)
        {
            return abbreviationMatches;
        }

        return shortcuts.Where(shortcut => MatchesForRootPalette(shortcut, trimmed));
    }

    private void EnsureLoaded(bool force = false)
    {
        EnsureConfigExists();

        var writeTime = File.GetLastWriteTimeUtc(ConfigPath);
        if (!force && writeTime == _lastWriteTimeUtc)
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(ConfigPath);
            if (fileInfo.Length > MaxConfigBytes)
            {
                RestoreLastGoodLayout();
                _lastWriteTimeUtc = writeTime;
                return;
            }

            if (!TryLoadLayoutFromFile(ConfigPath, out var loaded))
            {
                throw new InvalidDataException("Shortcut file could not be read.");
            }

            ApplyLoadedLayout(loaded);
            _lastWriteTimeUtc = writeTime;

            if (AssignMissingShortcutIds(_shortcuts))
            {
                WriteLayoutAtomic(_layout);
                _lastGoodLayout = CloneLayout(_layout);
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
            }
        }
        catch
        {
            RestoreLastGoodLayout();
            _lastWriteTimeUtc = writeTime;
        }
    }

    private void RestoreLastGoodLayout()
    {
        if (_lastGoodLayout.Count > 0)
        {
            ApplyLoadedLayout(CloneLayout(_lastGoodLayout));
            return;
        }

        _layout = [];
        _shortcuts = [];
    }

    private void ApplyLoadedLayout(List<ShortcutLayoutEntry> loaded)
    {
        _layout = NormalizeLayout(CloneLayout(loaded));
        SyncShortcutsFromLayout(_layout);
        _lastGoodLayout = CloneLayout(_layout);
    }

    private void EnsureConfigExists()
    {
        if (_configEnsured)
        {
            return;
        }

        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath) || !HasShortcutContent(ConfigPath))
        {
            if (TryImportShortcutsFromAlternateSources())
            {
                _configEnsured = true;
                return;
            }
        }

        if (!File.Exists(ConfigPath))
        {
            WriteLayoutAtomic([]);
            _lastGoodLayout = [];
            _layout = [];
            _shortcuts = [];
            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
        }

        _configEnsured = true;
    }

    private static bool HasShortcutContent(string path)
    {
        return TryLoadLayoutFromFile(path, out var layout) && CountValidShortcuts(layout) > 0;
    }

    private bool TryImportShortcutsFromAlternateSources()
    {
        foreach (var candidate in GetImportCandidatePaths())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (!TryLoadLayoutFromFile(candidate, out var layout) || CountValidShortcuts(layout) == 0)
            {
                continue;
            }

            ApplyLoadedLayout(layout);
            WriteLayoutAtomic(_layout);
            _lastGoodLayout = CloneLayout(_layout);
            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
            return true;
        }

        return false;
    }

    private IEnumerable<string> GetImportCandidatePaths()
    {
        yield return ConfigPath + ".bak";

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TerminalShortcutsCmdPal",
            "shortcuts.json");
    }

    private static bool TryLoadLayoutFromFile(string path, out List<ShortcutLayoutEntry> layout)
    {
        layout = [];

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxConfigBytes)
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            if (!ShortcutLayoutJson.TryParse(stream, out layout))
            {
                return false;
            }

            if (CountValidShortcuts(layout) > ShortcutValidation.MaxShortcutCount)
            {
                layout = [];
                return false;
            }

            layout = NormalizeLayout(layout);
            return true;
        }
        catch
        {
            layout = [];
            return false;
        }
    }

    private static async Task<(bool Success, List<ShortcutLayoutEntry> Layout)> TryLoadLayoutFromFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxConfigBytes)
            {
                return (false, []);
            }

            await using var stream = File.OpenRead(path);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShortcutLayoutJson.TryParse(stream, out var layout))
            {
                return (false, []);
            }

            if (CountValidShortcuts(layout) > ShortcutValidation.MaxShortcutCount)
            {
                return (false, []);
            }

            return (true, NormalizeLayout(layout));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (false, []);
        }
    }

    private void SaveLayoutLocked(List<ShortcutLayoutEntry> layout)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var normalized = NormalizeLayout(CloneLayout(layout));
        WriteLayoutAtomic(normalized);
        _layout = normalized;
        SyncShortcutsFromLayout(_layout);
        _lastGoodLayout = CloneLayout(_layout);
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
    }

    private void SchedulePersistLocked()
    {
        _persistPending = true;
        _persistTimer ??= new Timer(_ => WithLock(FlushPendingPersistLocked), null, Timeout.Infinite, Timeout.Infinite);
        _persistTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private void CancelPendingPersist()
    {
        _persistPending = false;
        _persistTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void FlushPendingPersistLocked()
    {
        if (!_persistPending)
        {
            return;
        }

        _persistPending = false;
        WriteLayoutAtomic(_layout);
        _lastGoodLayout = CloneLayout(_layout);
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
    }

    private void WriteLayoutAtomic(IReadOnlyList<ShortcutLayoutEntry> layout)
    {
        if (CountValidShortcuts(layout) > ShortcutValidation.MaxShortcutCount)
        {
            throw new InvalidOperationException($"At most {ShortcutValidation.MaxShortcutCount} shortcuts are supported.");
        }

        var payload = ShortcutLayoutJson.Serialize(layout);
        if (payload.Length > MaxConfigBytes)
        {
            throw new InvalidOperationException("Shortcut data is too large to save.");
        }

        Directory.CreateDirectory(ConfigDirectory);

        var tempPath = ConfigPath + ".tmp";
        var backupPath = ConfigPath + ".bak";

        if (!_fileMutex.WaitOne(TimeSpan.FromSeconds(5)))
        {
            throw new IOException("Could not acquire the shortcut store lock.");
        }

        try
        {
            File.WriteAllBytes(tempPath, payload);

            if (File.Exists(ConfigPath))
            {
                File.Replace(tempPath, ConfigPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, ConfigPath);
            }
        }
        finally
        {
            _fileMutex.ReleaseMutex();

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static List<ShortcutLayoutEntry> NormalizeLayout(IEnumerable<ShortcutLayoutEntry> layout)
    {
        var normalized = new List<ShortcutLayoutEntry>();
        foreach (var entry in layout)
        {
            if (entry.Kind == ShortcutLayoutEntryKind.Separator)
            {
                normalized.Add(ShortcutLayoutEntry.FromSeparator(entry.SeparatorTitle));
                continue;
            }

            if (entry.Shortcut is null || !IsValidShortcutEntry(entry.Shortcut))
            {
                continue;
            }

            var shortcut = Clone(entry.Shortcut);
            Normalize(shortcut);
            normalized.Add(ShortcutLayoutEntry.FromShortcut(shortcut));
        }

        AssignMissingShortcutIds(ShortcutLayoutJson.ExtractShortcuts(normalized));
        return normalized;
    }

    private void SyncShortcutsFromLayout(List<ShortcutLayoutEntry> layout)
    {
        _shortcuts = ShortcutLayoutJson.ExtractShortcuts(layout).Select(Clone).ToArray();
    }

    private static int CountValidShortcuts(IEnumerable<ShortcutLayoutEntry> layout) =>
        layout.Count(entry => entry.Kind == ShortcutLayoutEntryKind.Shortcut &&
                              entry.Shortcut is not null &&
                              IsValidShortcutEntry(entry.Shortcut));

    private static bool IsValidShortcutEntry(TerminalShortcut shortcut) =>
        !string.IsNullOrWhiteSpace(shortcut.Name) && !string.IsNullOrWhiteSpace(shortcut.Directory);

    private static ShortcutLayoutEntry? FindShortcutEntry(List<ShortcutLayoutEntry> layout, string name)
    {
        return layout.FirstOrDefault(entry =>
            entry.Kind == ShortcutLayoutEntryKind.Shortcut &&
            entry.Shortcut is not null &&
            entry.Shortcut.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RemoveShortcutEntry(List<ShortcutLayoutEntry> layout, string name) =>
        layout.RemoveAll(entry =>
            entry.Kind == ShortcutLayoutEntryKind.Shortcut &&
            entry.Shortcut is not null &&
            entry.Shortcut.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;

    private static List<ShortcutLayoutEntry> CloneLayout(IEnumerable<ShortcutLayoutEntry> layout) =>
        layout.Select(entry => entry.Kind switch
        {
            ShortcutLayoutEntryKind.Separator => ShortcutLayoutEntry.FromSeparator(entry.SeparatorTitle),
            _ => ShortcutLayoutEntry.FromShortcut(Clone(entry.Shortcut!)),
        }).ToList();

    private void RecordHistoryLayoutLocked(
        IReadOnlyList<ShortcutLayoutEntry> previous,
        IReadOnlyList<ShortcutLayoutEntry> next)
    {
        if (LayoutSnapshotEquals(NormalizeLayout(previous), NormalizeLayout(next)))
        {
            return;
        }

        PushLayoutHistory(_undoHistory, previous);
        _redoHistory.Clear();
    }

    private static void PushLayoutHistory(List<List<ShortcutLayoutEntry>> history, IEnumerable<ShortcutLayoutEntry> snapshot)
    {
        history.Add(CloneLayout(snapshot));
        if (history.Count > MaxHistoryEntries)
        {
            history.RemoveAt(0);
        }
    }

    private static bool LayoutSnapshotEquals(
        List<ShortcutLayoutEntry> left,
        List<ShortcutLayoutEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!LayoutEntryEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LayoutEntryEquals(ShortcutLayoutEntry left, ShortcutLayoutEntry right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        if (left.Kind == ShortcutLayoutEntryKind.Separator)
        {
            return string.Equals(left.SeparatorTitle, right.SeparatorTitle, StringComparison.Ordinal);
        }

        return left.Shortcut is not null &&
               right.Shortcut is not null &&
               ShortcutEquals(left.Shortcut, right.Shortcut);
    }

    private static TerminalShortcut[] OrderForDisplay(IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts
            .OrderByDescending(s => s.IsPinned)
            .ThenBy(s => s.PinOrder ?? int.MaxValue)
            .ThenByDescending(s => s.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int NextPinOrder(IEnumerable<TerminalShortcut> list) =>
        list.Where(s => s.IsPinned).Select(s => s.PinOrder ?? 0).DefaultIfEmpty().Max() + 1;

    private static void SetPinOrder(IEnumerable<TerminalShortcut> list, string name, int order)
    {
        var shortcut = list.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (shortcut is not null)
        {
            shortcut.PinOrder = order;
        }
    }

    private string GetDuplicateName(string sourceName) =>
        ResolveAvailableName(sourceName);

    public string ResolveAvailableName(string desiredName, string? replacingOriginalName = null)
    {
        var trimmed = desiredName.Trim();
        var existingNames = GetShortcuts()
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(replacingOriginalName))
        {
            var toRemove = existingNames
                .Where(name => name.Equals(replacingOriginalName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var name in toRemove)
            {
                existingNames.Remove(name);
            }
        }

        return GetUniqueName(trimmed, existingNames);
    }

    private static string GetUniqueName(string sourceName, HashSet<string> existingNames)
    {
        if (!existingNames.Contains(sourceName))
        {
            return sourceName;
        }

        var baseName = $"{sourceName} Copy";
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        var i = 2;
        while (true)
        {
            var candidate = $"{sourceName} Copy {i}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }

            i++;
        }
    }

    private static string BuildImportMessage(int imported, int skipped, int renamed)
    {
        var parts = new List<string> { $"Imported {imported} shortcut{(imported == 1 ? "" : "s")}." };

        if (renamed > 0)
        {
            parts.Add($"{renamed} renamed to avoid duplicates.");
        }

        if (skipped > 0)
        {
            parts.Add($"{skipped} skipped.");
        }

        return string.Join(" ", parts);
    }

    private static bool IsValidShortcut(TerminalShortcut shortcut) =>
        !string.IsNullOrWhiteSpace(shortcut.Name) && !string.IsNullOrWhiteSpace(shortcut.Directory);

    private static bool AssignMissingShortcutIds(TerminalShortcut[] shortcuts)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var shortcut in shortcuts)
        {
            if (!string.IsNullOrWhiteSpace(shortcut.Id) && usedIds.Add(shortcut.Id))
            {
                continue;
            }

            AssignShortcutId(shortcut, usedIds);
            changed = true;
        }

        return changed;
    }

    private static void AssignShortcutId(TerminalShortcut shortcut, IEnumerable<TerminalShortcut> existing)
    {
        var usedIds = existing
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssignShortcutId(shortcut, usedIds);
    }

    private static void AssignShortcutId(TerminalShortcut shortcut, HashSet<string> usedIds)
    {
        do
        {
            shortcut.Id = Guid.NewGuid().ToString("N");
        }
        while (!usedIds.Add(shortcut.Id));
    }

    private static bool Matches(TerminalShortcut shortcut, string query)
    {
        if (MatchesForRootPalette(shortcut, query))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Abbreviation))
        {
            if (shortcut.Abbreviation.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (shortcut.Abbreviation.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(shortcut.Command) &&
               shortcut.Command.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesForRootPalette(TerminalShortcut shortcut, string query)
    {
        if (shortcut.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (shortcut.Directory.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(shortcut.WtProfile) &&
               shortcut.WtProfile.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static TerminalShortcut Normalize(TerminalShortcut shortcut)
    {
        var terminal = (shortcut.Terminal ?? string.Empty).Trim().ToLowerInvariant();
        shortcut.Terminal = terminal switch
        {
            "wt" or "windows-terminal" => "wt",
            "it" or "intelligent-terminal" => "it",
            "wsl" => "wsl",
            "powershell" => "powershell",
            "pwsh" or "powershell7" => "pwsh",
            "cmd" => "cmd",
            "default" or "" => "default",
            _ => "default",
        };

        shortcut.WtProfile = string.IsNullOrWhiteSpace(shortcut.WtProfile) ? null : shortcut.WtProfile.Trim();

        if (!shortcut.IsPinned)
        {
            shortcut.PinOrder = null;
        }

        return shortcut;
    }

    private static TerminalShortcut[] CloneAll(IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts.Select(Clone).ToArray();

    private static bool ShortcutEquals(TerminalShortcut left, TerminalShortcut right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        string.Equals(left.Abbreviation, right.Abbreviation, StringComparison.Ordinal) &&
        string.Equals(left.Directory, right.Directory, StringComparison.Ordinal) &&
        string.Equals(left.Command, right.Command, StringComparison.Ordinal) &&
        string.Equals(left.Terminal, right.Terminal, StringComparison.Ordinal) &&
        string.Equals(left.WtProfile, right.WtProfile, StringComparison.Ordinal) &&
        left.RunAsAdmin == right.RunAsAdmin &&
        left.IsPinned == right.IsPinned &&
        left.PinOrder == right.PinOrder &&
        left.LastUsedUtc == right.LastUsedUtc;

    private static TerminalShortcut Clone(TerminalShortcut shortcut) => new()
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
    };

    private void WithLock(Action action)
    {
        _sync.Wait();
        try
        {
            action();
        }
        finally
        {
            _sync.Release();
        }
    }

    private T WithLock<T>(Func<T> action)
    {
        _sync.Wait();
        try
        {
            return action();
        }
        finally
        {
            _sync.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            WithLock(FlushPendingPersistLocked);
        }
        catch
        {
            // Best effort flush during shutdown.
        }

        _persistTimer?.Dispose();
        _sync.Dispose();
        _fileMutex.Dispose();
        GC.SuppressFinalize(this);
    }
}


