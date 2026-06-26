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

internal sealed class ShortcutRepository : IShortcutRepository
{
    private const int MaxConfigBytes = 2 * 1024 * 1024;
    private const int MaxHistoryEntries = 50;

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Mutex _fileMutex = new(false, @"Global\QuickShell_shortcuts_json");

    private TerminalShortcut[] _shortcuts = [];
    private TerminalShortcut[] _lastGoodShortcuts = [];
    private readonly List<TerminalShortcut[]> _undoHistory = [];
    private readonly List<TerminalShortcut[]> _redoHistory = [];
    private DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private bool _configEnsured;
    private bool _persistPending;
    private Timer? _persistTimer;

    public string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickShell");

    public string ConfigPath => Path.Combine(ConfigDirectory, "shortcuts.json");

    public IReadOnlyList<TerminalShortcut> GetShortcuts() =>
        WithLock(() =>
        {
            EnsureLoaded();
            return CloneAll(_shortcuts);
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

                var shortcuts = CloneAll(_shortcuts);
                var preparedPayload = JsonSerializer.SerializeToUtf8Bytes(shortcuts, QuickShellJsonContext.Default.TerminalShortcutArray);
                if (preparedPayload.Length > MaxConfigBytes)
                {
                    return (Success: false, Payload: Array.Empty<byte>());
                }

                return (Success: true, Payload: preparedPayload);
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
            var (loaded, shortcuts) = await TryLoadShortcutsFromFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (!loaded || shortcuts.Length == 0)
            {
                return new ShortcutImportReadResult(false, [], "No valid shortcuts were found in that file.");
            }

            return new ShortcutImportReadResult(true, shortcuts, string.Empty);
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
        if (!TryReadImportFile(path, out var imported, out var error))
        {
            return new ShortcutTransferResult
            {
                Success = false,
                Message = error,
            };
        }

        return ImportReplaceCore(imported);
    }

    public async Task<ShortcutTransferResult> ImportReplaceAsync(string path, CancellationToken cancellationToken = default)
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
            return ImportReplaceCore(readResult.Shortcuts);
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

    private ShortcutTransferResult ImportMergeCore(TerminalShortcut[] imported) =>
        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneAll(_shortcuts);
            var list = _shortcuts.Select(Clone).ToList();
            var existingNames = list.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                AssignShortcutId(shortcut, list);

                if (shortcut.IsPinned && shortcut.PinOrder is null)
                {
                    shortcut.PinOrder = NextPinOrder(list);
                }

                list.Add(shortcut);
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

            if (list.Count > ShortcutValidation.MaxShortcutCount)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = $"Import would exceed the {ShortcutValidation.MaxShortcutCount}-shortcut limit.",
                };
            }

            RecordHistoryLocked(previous, list);
            SaveLocked(list);

            return new ShortcutTransferResult
            {
                Success = true,
                Message = BuildImportMessage(importedCount, skipped, renamed),
                Imported = importedCount,
                Skipped = skipped,
                Renamed = renamed,
            };
        });

    private ShortcutTransferResult ImportReplaceCore(TerminalShortcut[] imported) =>
        WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneAll(_shortcuts);
            var valid = new List<TerminalShortcut>(imported.Length);
            var skipped = 0;

            foreach (var source in imported)
            {
                var shortcut = Clone(source);
                shortcut.LastUsedUtc = null;

                if (!ShortcutValidation.TryValidateForImport(shortcut, out _))
                {
                    skipped++;
                    continue;
                }

                valid.Add(shortcut);
            }

            if (valid.Count == 0)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = "No shortcuts could be imported from that file.",
                    Skipped = skipped,
                };
            }

            if (valid.Count > ShortcutValidation.MaxShortcutCount)
            {
                return new ShortcutTransferResult
                {
                    Success = false,
                    Message = $"Import exceeds the {ShortcutValidation.MaxShortcutCount}-shortcut limit.",
                };
            }

            RecordHistoryLocked(previous, valid);
            SaveLocked(valid);

            return new ShortcutTransferResult
            {
                Success = true,
                Message = BuildImportMessage(valid.Count, skipped, renamed: 0),
                Imported = valid.Count,
                Skipped = skipped,
            };
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

            var current = CloneAll(_shortcuts);
            var previous = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            PushHistory(_redoHistory, current);
            SaveLocked(previous);
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

            var current = CloneAll(_shortcuts);
            var next = _redoHistory[^1];
            _redoHistory.RemoveAt(_redoHistory.Count - 1);
            PushHistory(_undoHistory, current);
            SaveLocked(next);
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
            var previous = CloneAll(_shortcuts);

            var list = _shortcuts.Select(Clone).ToList();

            var existing = list.FirstOrDefault(s =>
                s.Name.Equals(shortcut.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(originalName) && s.Name.Equals(originalName, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
            {
                shortcut.Id = existing.Id;
                shortcut.IsPinned = existing.IsPinned;
                shortcut.PinOrder = existing.PinOrder;
                shortcut.LastUsedUtc = existing.LastUsedUtc;
            }
            else
            {
                AssignShortcutId(shortcut, list);
            }

            if (!string.IsNullOrWhiteSpace(originalName))
            {
                list.RemoveAll(s => s.Name.Equals(originalName, StringComparison.OrdinalIgnoreCase));
            }

            list.RemoveAll(s => s.Name.Equals(shortcut.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(Clone(shortcut));

            if (shortcut.IsPinned && shortcut.PinOrder is null)
            {
                SetPinOrder(list, shortcut.Name, NextPinOrder(list));
            }

            RecordHistoryLocked(previous, list);
            SaveLocked(list);
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
            var previous = CloneAll(_shortcuts);

            var list = _shortcuts.Select(Clone).ToList();
            var removed = list.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                RecordHistoryLocked(previous, list);
                SaveLocked(list);
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
            var previous = CloneAll(_shortcuts);

            var list = _shortcuts.Select(Clone).ToList();
            var shortcut = list.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (shortcut is null)
            {
                return false;
            }

            shortcut.IsPinned = !shortcut.IsPinned;
            shortcut.PinOrder = shortcut.IsPinned ? NextPinOrder(list) : null;
            RecordHistoryLocked(previous, list);
            SaveLocked(list);
            return shortcut.IsPinned;
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
            var previous = CloneAll(_shortcuts);

            var list = _shortcuts.Select(Clone).ToList();
            var pinned = list
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
                SetPinOrder(list, pinned[i].Name, i + 1);
            }

            RecordHistoryLocked(previous, list);
            SaveLocked(list);
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

            var list = _shortcuts.Select(Clone).ToList();
            var shortcut = list.FirstOrDefault(s => s.Id.Equals(shortcutId, StringComparison.OrdinalIgnoreCase));
            if (shortcut is null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (shortcut.LastUsedUtc is not null && (now - shortcut.LastUsedUtc.Value).TotalSeconds < 2)
            {
                return;
            }

            shortcut.LastUsedUtc = now;
            _shortcuts = OrderForDisplay(list);
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
                _shortcuts = _lastGoodShortcuts.Length > 0 ? CloneAll(_lastGoodShortcuts) : [];
                _lastWriteTimeUtc = writeTime;
                return;
            }

            if (!TryLoadShortcutsFromFile(ConfigPath, out var loaded))
            {
                throw new InvalidDataException("Shortcut file could not be read.");
            }

            _shortcuts = loaded;
            _lastGoodShortcuts = CloneAll(_shortcuts);
            _lastWriteTimeUtc = writeTime;

            if (AssignMissingShortcutIds(_shortcuts))
            {
                WriteShortcutsAtomic(_shortcuts);
                _lastGoodShortcuts = CloneAll(_shortcuts);
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
            }
        }
        catch
        {
            _shortcuts = _lastGoodShortcuts.Length > 0 ? CloneAll(_lastGoodShortcuts) : [];
            _lastWriteTimeUtc = writeTime;
        }
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
            var empty = Array.Empty<TerminalShortcut>();
            WriteShortcutsAtomic(empty);
            _lastGoodShortcuts = empty;
            _shortcuts = empty;
            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
        }

        _configEnsured = true;
    }

    private bool HasShortcutContent(string path)
    {
        return TryLoadShortcutsFromFile(path, out var shortcuts) && shortcuts.Length > 0;
    }

    private bool TryImportShortcutsFromAlternateSources()
    {
        foreach (var candidate in GetImportCandidatePaths())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (!TryLoadShortcutsFromFile(candidate, out var shortcuts) || shortcuts.Length == 0)
            {
                continue;
            }

            WriteShortcutsAtomic(shortcuts);
            _lastGoodShortcuts = CloneAll(shortcuts);
            _shortcuts = shortcuts;
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

    private bool TryLoadShortcutsFromFile(string path, out TerminalShortcut[] shortcuts)
    {
        shortcuts = [];

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxConfigBytes)
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            var parsed = JsonSerializer.Deserialize(stream, QuickShellJsonContext.Default.ListTerminalShortcut);
            if (parsed is null)
            {
                return false;
            }

            if (parsed.Count > ShortcutValidation.MaxShortcutCount)
            {
                return false;
            }

            shortcuts = OrderForDisplay(
                parsed
                    .Where(IsValidShortcut)
                    .Select(Normalize)
                    .Select(Clone)
                    .ToArray());

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool Success, TerminalShortcut[] Shortcuts)> TryLoadShortcutsFromFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxConfigBytes)
            {
                return (false, []);
            }

            await using var stream = File.OpenRead(path);
            var parsed = await JsonSerializer.DeserializeAsync(stream, QuickShellJsonContext.Default.ListTerminalShortcut, cancellationToken).ConfigureAwait(false);
            if (parsed is null)
            {
                return (false, []);
            }

            if (parsed.Count > ShortcutValidation.MaxShortcutCount)
            {
                return (false, []);
            }

            var shortcuts = OrderForDisplay(
                parsed
                    .Where(IsValidShortcut)
                    .Select(Normalize)
                    .Select(Clone)
                    .ToArray());

            return (true, shortcuts);
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

    private void SaveLocked(IReadOnlyCollection<TerminalShortcut> shortcuts)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var ordered = OrderForDisplay(shortcuts.Where(IsValidShortcut).Select(Normalize).Select(Clone)).ToArray();
        AssignMissingShortcutIds(ordered);
        WriteShortcutsAtomic(ordered);
        _shortcuts = ordered;
        _lastGoodShortcuts = CloneAll(ordered);
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
        WriteShortcutsAtomic(_shortcuts);
        _lastGoodShortcuts = CloneAll(_shortcuts);
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
    }

    private void WriteShortcutsAtomic(TerminalShortcut[] shortcuts)
    {
        if (shortcuts.Length > ShortcutValidation.MaxShortcutCount)
        {
            throw new InvalidOperationException($"At most {ShortcutValidation.MaxShortcutCount} shortcuts are supported.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(shortcuts, QuickShellJsonContext.Default.TerminalShortcutArray);
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

    private TerminalShortcut[] OrderForDisplay(IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts
            .OrderByDescending(s => s.IsPinned)
            .ThenBy(s => s.PinOrder ?? int.MaxValue)
            .ThenByDescending(s => s.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private int NextPinOrder(IEnumerable<TerminalShortcut> list) =>
        list.Where(s => s.IsPinned).Select(s => s.PinOrder ?? 0).DefaultIfEmpty().Max() + 1;

    private void SetPinOrder(List<TerminalShortcut> list, string name, int order)
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

    private string GetUniqueName(string sourceName, HashSet<string> existingNames)
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

    private string BuildImportMessage(int imported, int skipped, int renamed)
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

    private bool IsValidShortcut(TerminalShortcut shortcut) =>
        !string.IsNullOrWhiteSpace(shortcut.Name) && !string.IsNullOrWhiteSpace(shortcut.Directory);

    private bool AssignMissingShortcutIds(TerminalShortcut[] shortcuts)
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

    private void AssignShortcutId(TerminalShortcut shortcut, IEnumerable<TerminalShortcut> existing)
    {
        var usedIds = existing
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssignShortcutId(shortcut, usedIds);
    }

    private void AssignShortcutId(TerminalShortcut shortcut, HashSet<string> usedIds)
    {
        do
        {
            shortcut.Id = Guid.NewGuid().ToString("N");
        }
        while (!usedIds.Add(shortcut.Id));
    }

    private bool Matches(TerminalShortcut shortcut, string query)
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

    private TerminalShortcut Normalize(TerminalShortcut shortcut)
    {
        var terminal = (shortcut.Terminal ?? string.Empty).Trim().ToLowerInvariant();
        shortcut.Terminal = terminal switch
        {
            "wt" or "windows-terminal" => "wt",
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

    private TerminalShortcut[] CloneAll(IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts.Select(Clone).ToArray();

    private void RecordHistoryLocked(
        IReadOnlyCollection<TerminalShortcut> previous,
        IReadOnlyCollection<TerminalShortcut> next)
    {
        var orderedNext = OrderForDisplay(next.Where(IsValidShortcut).Select(Normalize).Select(Clone));
        if (SnapshotEquals(previous, orderedNext))
        {
            return;
        }

        PushHistory(_undoHistory, previous);
        _redoHistory.Clear();
    }

    private void PushHistory(List<TerminalShortcut[]> history, IEnumerable<TerminalShortcut> snapshot)
    {
        history.Add(CloneAll(snapshot));
        if (history.Count > MaxHistoryEntries)
        {
            history.RemoveAt(0);
        }
    }

    private bool SnapshotEquals(IReadOnlyCollection<TerminalShortcut> left, TerminalShortcut[] right)
    {
        if (left.Count != right.Length)
        {
            return false;
        }

        var leftArray = left as TerminalShortcut[] ?? left.ToArray();
        for (var i = 0; i < leftArray.Length; i++)
        {
            if (!ShortcutEquals(leftArray[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool ShortcutEquals(TerminalShortcut left, TerminalShortcut right) =>
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

    private TerminalShortcut Clone(TerminalShortcut shortcut) => new()
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
}


