using QuickShell.Models;
using System.Diagnostics;
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
    private const int MaxHistoryEntries = 25;

    private readonly string? _configDirectoryOverride;
    private readonly IAtomicFileWriter _fileWriter;

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowOperationThreshold = TimeSpan.FromSeconds(2);
    private const int FileMutexTimeoutSeconds = 3;

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Mutex _fileMutex = new(false, @"Global\QuickShell_shortcuts_json");

    private TerminalShortcut[] _shortcuts = [];
    private List<ShortcutLayoutEntry> _layout = [];
    private List<ShortcutLayoutEntry> _lastGoodLayout = [];
    private long _snapshotVersion;
    private long _structuralVersion;
    private WorkspaceRepositorySnapshot _cachedSnapshot;
    private readonly Dictionary<string, TerminalShortcut> _shortcutsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TerminalShortcut> _shortcutsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkspaceSecurityMetadata> _pendingDuplicateSecurity = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<List<ShortcutLayoutEntry>> _undoHistory = [];
    private readonly List<List<ShortcutLayoutEntry>> _redoHistory = [];
    private DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private bool _configEnsured;
    private bool _persistPending;
    private System.Threading.Timer? _persistTimer;
    private bool _disposed;

    public event EventHandler? WorkspacesChanged;

    public ShortcutRepository()
        : this(configDirectory: null)
    {
    }

    internal ShortcutRepository(string? configDirectory)
        : this(configDirectory, writer: null)
    {
    }

    internal ShortcutRepository(string? configDirectory, IAtomicFileWriter? writer)
    {
        _configDirectoryOverride = configDirectory;
        _fileWriter = writer ?? new AtomicFileWriter();
    }

    public string ConfigDirectory =>
        _configDirectoryOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickShell");

    public string ConfigPath => Path.Combine(ConfigDirectory, "shortcuts.json");

    public IReadOnlyList<TerminalShortcut> GetShortcuts() => GetSnapshot().Shortcuts;

    public IReadOnlyList<ShortcutLayoutEntry> GetLayout() => GetSnapshot().Layout;

    public WorkspaceRepositorySnapshot GetSnapshot()
    {
        AcquireLockOrThrow(nameof(GetSnapshot));
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            EnsureLoaded();
            if (_cachedSnapshot.Version != _snapshotVersion)
            {
                var snapshotShortcuts = CloneAll(_shortcuts);
                var snapshotLayout = CloneLayout(_layout);
                _cachedSnapshot = new WorkspaceRepositorySnapshot(
                    _snapshotVersion,
                    Array.AsReadOnly(snapshotShortcuts),
                    snapshotLayout.AsReadOnly(),
                    _undoHistory.Count > 0,
                    _redoHistory.Count > 0,
                    _structuralVersion);
            }

            return _cachedSnapshot;
        }
        finally
        {
            ReleaseLockAndReportSlow(nameof(GetSnapshot), startTimestamp);
        }
    }

    public TerminalShortcut? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            return _shortcutsByName.TryGetValue(name, out var shortcut) ? Clone(shortcut) : null;
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
            return _shortcutsById.TryGetValue(id, out var shortcut) ? Clone(shortcut) : null;
        });
    }

    public StoredWorkspace? GetStoredWorkspace(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            var entry = _layout.FirstOrDefault(candidate =>
                candidate.Kind == ShortcutLayoutEntryKind.Shortcut
                && candidate.Shortcut is not null
                && candidate.Shortcut.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (entry?.Shortcut is null)
            {
                return null;
            }

            var security = entry.Security ?? new WorkspaceSecurityMetadata();
            return new StoredWorkspace(
                WorkspaceClone.Clone(entry.Shortcut),
                security with { },
                Math.Max(1, security.Revision));
        });
    }

    public WorkspaceReviewSnapshot BeginTrustReview(string workspaceId)
    {
        var stored = GetStoredWorkspace(workspaceId);
        if (stored is null)
        {
            return new WorkspaceReviewSnapshot(
                null,
                null,
                new WorkspaceAuthorizationResult(
                    false,
                    WorkspaceIssueCode.WorkspaceNotFound,
                    [new(WorkspaceIssueCode.WorkspaceNotFound, "Workspace was not found.")],
                    [],
                    new WorkspaceEffectiveValues(null, null, null, null, null, null),
                    0));
        }

        var reviewWorkspace = stored with
        {
            Content = WorkspaceClone.Clone(stored.Content),
            Security = stored.Security with { },
        };
        var assessment = WorkspaceSecurityPolicy.Authorize(reviewWorkspace, WorkspaceAction.GrantTrust);
        return new WorkspaceReviewSnapshot(
            stored,
            WorkspaceSecurityPolicy.CreateReviewToken(reviewWorkspace),
            assessment);
    }

    public TrustTransitionResult GrantTrust(string workspaceId, WorkspaceReviewToken reviewToken) =>
        WithLock(() =>
        {
            EnsureLoaded();
            var entry = FindEntryById(_layout, workspaceId);
            if (entry?.Shortcut is null)
            {
                return new TrustTransitionResult(TrustTransitionStatus.WorkspaceNotFound, "Workspace was not found.");
            }

            var stored = ToStoredWorkspace(entry);
            if (stored.Security.IsTrusted)
            {
                return new TrustTransitionResult(
                    TrustTransitionStatus.AlreadyInRequestedState,
                    "Workspace is already trusted.");
            }

            if (reviewToken is null || !WorkspaceSecurityPolicy.MatchesReviewToken(stored, reviewToken))
            {
                return new TrustTransitionResult(
                    TrustTransitionStatus.WorkspaceChangedSinceReview,
                    "Workspace changed since it was reviewed. Review it again before granting trust.");
            }

            var assessment = WorkspaceSecurityPolicy.Authorize(stored, WorkspaceAction.GrantTrust);
            if (assessment.Issues.Count > 0)
            {
                return new TrustTransitionResult(
                    TrustTransitionStatus.WorkspaceInvalid,
                    assessment.Issues[0].Message);
            }

            var beforeTransition = CloneLayout(_layout);
            var trustedSecurity = entry.Security ?? new WorkspaceSecurityMetadata();
            entry.Security = trustedSecurity with
            {
                IsTrusted = true,
                Revision = Math.Max(1, trustedSecurity.Revision) + 1,
            };
            try
            {
                SaveLayoutLocked(CloneLayout(_layout), preserveRepositorySecurity: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _layout = beforeTransition;
                SyncShortcutsFromLayout(_layout);
                return new TrustTransitionResult(TrustTransitionStatus.PersistenceFailed, ex.Message);
            }
            return new TrustTransitionResult(TrustTransitionStatus.Granted, "Workspace trusted.");
        });

    public TrustTransitionResult RevokeTrust(string workspaceId) =>
        WithLock(() =>
        {
            EnsureLoaded();
            var entry = FindEntryById(_layout, workspaceId);
            if (entry?.Shortcut is null)
            {
                return new TrustTransitionResult(TrustTransitionStatus.WorkspaceNotFound, "Workspace was not found.");
            }

            var currentSecurity = entry.Security ?? new WorkspaceSecurityMetadata();
            if (!currentSecurity.IsTrusted)
            {
                return new TrustTransitionResult(
                    TrustTransitionStatus.AlreadyInRequestedState,
                    "Workspace is already untrusted.");
            }

            var beforeTransition = CloneLayout(_layout);
            entry.Security = currentSecurity with
            {
                IsTrusted = false,
                Revision = Math.Max(1, currentSecurity.Revision) + 1,
            };
            try
            {
                SaveLayoutLocked(CloneLayout(_layout), preserveRepositorySecurity: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _layout = beforeTransition;
                SyncShortcutsFromLayout(_layout);
                return new TrustTransitionResult(TrustTransitionStatus.PersistenceFailed, ex.Message);
            }
            return new TrustTransitionResult(TrustTransitionStatus.Revoked, "Workspace trust revoked.");
        });

    public TerminalShortcut? GetByNameReadOnly(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            return _shortcutsByName.TryGetValue(name, out var shortcut) ? shortcut : null;
        });
    }

    public TerminalShortcut? GetByIdReadOnly(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            return _shortcutsById.TryGetValue(id, out var shortcut) ? shortcut : null;
        });
    }

    public TerminalShortcut? ResolveForOpenCommand(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            if (_shortcutsById.TryGetValue(key, out var shortcut))
            {
                return Clone(shortcut);
            }

            if (CommandDescriptor.TryDecodeLegacyNameKey(key, out var legacyName) &&
                _shortcutsByName.TryGetValue(legacyName, out shortcut))
            {
                return Clone(shortcut);
            }

            return null;
        });
    }

    public void Reload() =>
        WithLock(() =>
        {
            CancelPendingPersist();
            _lastWriteTimeUtc = DateTime.MinValue;
            EnsureLoaded(force: true);
        });

    public Task PreloadAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(() => EnsureLoadedAsync(force: false, cancellationToken), cancellationToken);

    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        WithLockAsync(async () =>
        {
            CancelPendingPersist();
            _lastWriteTimeUtc = DateTime.MinValue;
            await EnsureLoadedAsync(force: true, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

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

        return WithLock(() =>
        {
            EnsureLoaded();
            return imported.Count(shortcut =>
                !string.IsNullOrWhiteSpace(shortcut.Name) &&
                _shortcutsByName.ContainsKey(shortcut.Name));
        });
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

                layout.Add(ShortcutLayoutEntry.FromShortcut(
                    shortcut,
                    new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 1 }));
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

                valid.Add(ShortcutLayoutEntry.FromShortcut(
                    shortcut,
                    new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 1 }));
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

            RecordHistoryLayoutLocked(previous, valid);
            SaveLayoutLocked(valid, preserveRepositorySecurity: false);

            return new ShortcutTransferResult
            {
                Success = true,
                Message = BuildImportMessage(CountValidShortcuts(valid), skipped, renamed: 0),
                Imported = CountValidShortcuts(valid),
                Skipped = skipped,
            };
        });

    public ShortcutTransferResult ResetAll() =>
        WithLock(() =>
        {
            EnsureLoaded();
            if (CountValidShortcuts(_layout) == 0)
            {
                return new ShortcutTransferResult
                {
                    Success = true,
                    Message = "No workspaces to reset.",
                };
            }

            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var empty = new List<ShortcutLayoutEntry>();
            RecordHistoryLayoutLocked(previous, empty);
            SaveLayoutLocked(empty);

            return new ShortcutTransferResult
            {
                Success = true,
                Message = "Reset all workspaces. Use Undo (Ctrl+Z) if you change your mind.",
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
        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        if (!ShortcutValidation.TryValidate(shortcut, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        if (!ShortcutValidation.TryValidateUniqueName(shortcut.Name, originalName, this, out validationError))
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
            ShortcutLaunchNormalization.NormalizeShortcut(cloned);

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
                if (!_pendingDuplicateSecurity.ContainsKey(cloned.Id))
                {
                    AssignShortcutId(cloned, ShortcutLayoutJson.ExtractShortcuts(layout));
                }
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
                layout.Add(ShortcutLayoutEntry.FromShortcut(
                    cloned,
                    _pendingDuplicateSecurity.Remove(cloned.Id, out var duplicateSecurity)
                        ? duplicateSecurity
                        : new WorkspaceSecurityMetadata { IsTrusted = true, Revision = 1 }));
            }

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout, trustNewEntries: true);
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

    public bool MovePinned(string name, int direction) =>
        MovePinnedCore(direction, match: s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool MovePinnedToEdge(string name, bool toTop) =>
        MovePinnedToEdgeCore(toTop, match: s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool MovePinnedById(string id, int direction) =>
        MovePinnedCore(
            direction,
            match: s => !string.IsNullOrWhiteSpace(id)
                && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool MovePinnedToEdgeById(string id, bool toTop) =>
        MovePinnedToEdgeCore(
            toTop,
            match: s => !string.IsNullOrWhiteSpace(id)
                && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private bool MovePinnedCore(int direction, Func<TerminalShortcut, bool> match)
    {
        if (direction == 0)
        {
            return false;
        }

        return WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var pinned = GetPinnedOrdered(layout);

            var index = pinned.FindIndex(s => match(s));
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
            // Re-number every favorite so sort order is unambiguous (null PinOrder
            // was collapsing many favorites into name order and making moves look inert).
            RenumberPinned(pinned);

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);
            return true;
        });
    }

    private bool MovePinnedToEdgeCore(bool toTop, Func<TerminalShortcut, bool> match)
    {
        return WithLock(() =>
        {
            EnsureLoaded();
            CancelPendingPersist();
            var previous = CloneLayout(_layout);
            var layout = CloneLayout(_layout);
            var pinned = GetPinnedOrdered(layout);

            var index = pinned.FindIndex(s => match(s));
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
            RenumberPinned(pinned);

            RecordHistoryLayoutLocked(previous, layout);
            SaveLayoutLocked(layout);
            return true;
        });
    }

    private static List<TerminalShortcut> GetPinnedOrdered(List<ShortcutLayoutEntry> layout) =>
        ShortcutLayoutJson.ExtractShortcuts(layout)
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void RenumberPinned(List<TerminalShortcut> pinned)
    {
        for (var i = 0; i < pinned.Count; i++)
        {
            pinned[i].IsPinned = true;
            pinned[i].PinOrder = i + 1;
        }
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
            // Usage-only change: bump _snapshotVersion (UI staleness) but not
            // _structuralVersion, so the launch plan cache does not thrash on
            // every repeat launch.
            SyncShortcutsFromLayout(_layout, bumpStructuralVersion: false);
            SchedulePersistLocked();
        });
    }

    public TerminalShortcut? BuildDuplicate(string name)
    {
        var source = GetByName(name);
        return source is null ? null : BuildDuplicateFrom(source);
    }

    public TerminalShortcut BuildDuplicateFrom(TerminalShortcut source)
    {
        var copy = Clone(source);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = GetDuplicateName(copy.Name);
        copy.IsPinned = false;
        copy.PinOrder = null;
        copy.LastUsedUtc = null;

        var sourceSecurity = GetStoredWorkspace(source.Id)?.Security;
        if (sourceSecurity is not null)
        {
            _pendingDuplicateSecurity[copy.Id] = sourceSecurity with { Revision = 1 };
        }

        return copy;
    }

    public IEnumerable<TerminalShortcut> Search(string query) => GetSnapshot().Search(query);

    public IEnumerable<TerminalShortcut> SearchForRootPalette(string query) =>
        GetSnapshot().SearchForRootPalette(query);

    public IEnumerable<WorkspaceTaskAction> SearchTaskActions(string query) =>
        GetSnapshot().SearchTaskActions(query);

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

    private async Task EnsureLoadedAsync(bool force, CancellationToken cancellationToken)
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

            var (loaded, layout) = await TryLoadLayoutFromFileAsync(ConfigPath, cancellationToken).ConfigureAwait(false);
            if (!loaded)
            {
                throw new InvalidDataException("Shortcut file could not be read.");
            }

            ApplyLoadedLayout(layout);
            _lastWriteTimeUtc = writeTime;

            if (AssignMissingShortcutIds(_shortcuts))
            {
                WriteLayoutAtomic(_layout);
                _lastGoodLayout = CloneLayout(_layout);
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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

        _snapshotVersion++;
        _structuralVersion++;
        _layout = [];
        _shortcuts = [];
        RebuildShortcutIndexes();
    }

    private void ApplyLoadedLayout(List<ShortcutLayoutEntry> loaded)
    {
        _layout = NormalizeLayout(CloneLayout(loaded));
        SyncShortcutsFromLayout(_layout);
        _lastGoodLayout = CloneLayout(_layout);
        TryMigrateLegacyWorkspacesLocked();
    }

    private void TryMigrateLegacyWorkspacesLocked()
    {
        if (!WorkspaceLegacyMigration.TryReadLegacyWorkspaces(ConfigDirectory, this, out var imported, out _))
        {
            return;
        }

        if (imported.Count == 0)
        {
            WorkspaceLegacyMigration.ArchiveWorkspacesFile(ConfigDirectory);
            return;
        }

        var layout = CloneLayout(_layout);
        var shortcuts = ShortcutLayoutJson.ExtractShortcuts(layout).ToList();
        var changed = false;

        foreach (var migrated in imported)
        {
            if (shortcuts.Any(existing => existing.Name.Equals(migrated.Name, StringComparison.OrdinalIgnoreCase)))
            {
                migrated.Name = WorkspaceLegacyMigration.ResolveAvailableName(migrated.Name, shortcuts);
            }

            ShortcutLaunchNormalization.NormalizeShortcut(migrated);
            AssignShortcutId(migrated, shortcuts);
            shortcuts.Add(migrated);
            layout.Add(ShortcutLayoutEntry.FromShortcut(Clone(migrated)));
            changed = true;
        }

        if (!changed)
        {
            WorkspaceLegacyMigration.ArchiveWorkspacesFile(ConfigDirectory);
            return;
        }

        SaveLayoutLocked(layout, trustNewEntries: true);
        WorkspaceLegacyMigration.ArchiveWorkspacesFile(ConfigDirectory);
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
            _snapshotVersion++;
            _structuralVersion++;
            _lastGoodLayout = [];
            _layout = [];
            _shortcuts = [];
            RebuildShortcutIndexes();
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

            // Alternate files are restore/import ingress, so their content must
            // not inherit authority from the source file or a local collision.
            layout = layout
                .Select(entry => entry.Kind == ShortcutLayoutEntryKind.Shortcut && entry.Shortcut is not null
                    ? ShortcutLayoutEntry.FromShortcut(
                        Clone(entry.Shortcut),
                        new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 1 })
                    : ShortcutLayoutEntry.FromSeparator(entry.SeparatorTitle))
                .ToList();
            ApplyLoadedLayout(layout);
            WriteLayoutAtomic(_layout);
            _lastGoodLayout = CloneLayout(_layout);
            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
            RaiseWorkspacesChanged();
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

    /// <summary>Matches the async loader's retry budget for transient sharing violations.</summary>
    private const int LoadRetryAttempts = 3;
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromMilliseconds(50);

    private static bool TryLoadLayoutFromFile(string path, out List<ShortcutLayoutEntry> layout)
    {
        for (var attempt = 1; attempt <= LoadRetryAttempts; attempt++)
        {
            if (TryLoadLayoutFromFileOnce(path, out layout, out var transient))
            {
                return true;
            }

            if (!transient || attempt == LoadRetryAttempts)
            {
                return false;
            }

            Thread.Sleep(LoadRetryDelay);
        }

        layout = [];
        return false;
    }

    private static bool TryLoadLayoutFromFileOnce(string path, out List<ShortcutLayoutEntry> layout, out bool transient)
    {
        layout = [];
        transient = false;

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxConfigBytes)
            {
                return false;
            }

            // FileShare.ReadWrite matches TryLoadLayoutFromFileAsync below: a fresh process's
            // first read can race a just-completed File.Replace from the previous process (or
            // an AV/indexer scan touching the file), and FileShare.Read-only would throw a
            // sharing violation exactly then — right after every redeploy, on the one read that
            // matters most. That failure fell straight through to an empty in-memory layout with
            // no retry, which then got persisted as soon as the user made any edit, silently
            // discarding real data still sitting untouched on disk.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            layout = [];
            transient = true;
            return false;
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

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                useAsync: true);
            var (parsed, layout) = await ShortcutLayoutJson.TryParseAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!parsed)
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

    private void SaveLayoutLocked(
        List<ShortcutLayoutEntry> layout,
        bool preserveRepositorySecurity = true,
        bool trustNewEntries = false)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var prepared = preserveRepositorySecurity
            ? MergeRepositorySecurity(layout, trustNewEntries)
            : CloneLayout(layout);
        var normalized = NormalizeLayout(prepared);
        WriteLayoutAtomic(normalized);
        _layout = normalized;
        SyncShortcutsFromLayout(_layout);
        _lastGoodLayout = CloneLayout(_layout);
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
        RaiseWorkspacesChanged();
    }

    private List<ShortcutLayoutEntry> MergeRepositorySecurity(
        List<ShortcutLayoutEntry> layout,
        bool trustNewEntries)
    {
        var result = new List<ShortcutLayoutEntry>(layout.Count);
        foreach (var entry in layout)
        {
            if (entry.Kind == ShortcutLayoutEntryKind.Separator)
            {
                result.Add(ShortcutLayoutEntry.FromSeparator(entry.SeparatorTitle));
                continue;
            }

            if (entry.Shortcut is null)
            {
                continue;
            }

            var incoming = ShortcutLayoutEntry.FromShortcut(
                Clone(entry.Shortcut),
                entry.Security ?? new WorkspaceSecurityMetadata());
            var current = FindEntryById(_layout, entry.Shortcut.Id);
            if (current?.Shortcut is not null)
            {
                var currentSecurity = current.Security is null
                    ? new WorkspaceSecurityMetadata()
                    : current.Security with { };
                if (!ShortcutEquals(current.Shortcut, entry.Shortcut))
                {
                    currentSecurity = currentSecurity with
                    {
                        Revision = Math.Max(1, currentSecurity.Revision) + 1,
                    };
                }

                incoming.Security = currentSecurity;
            }
            else if (!trustNewEntries)
            {
                var security = incoming.Security ?? new WorkspaceSecurityMetadata();
                security = security with
                {
                    IsTrusted = false,
                    Revision = Math.Max(1, security.Revision),
                };
                incoming.Security = security;
            }

            result.Add(incoming);
        }

        return result;
    }

    private void SchedulePersistLocked()
    {
        _persistPending = true;
        _persistTimer ??= new System.Threading.Timer(_ => RunScheduledPersist(), null, Timeout.Infinite, Timeout.Infinite);
        _persistTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private void RunScheduledPersist()
    {
        try
        {
            WithLock(FlushPendingPersistLocked);
        }
        catch (TimeoutException)
        {
            // The lock was held by someone else when this fired; _persistPending is still
            // true (FlushPendingPersistLocked never ran), so reschedule instead of silently
            // dropping the deferred write.
            try
            {
                _persistTimer?.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Disposed between the throw and here; Dispose()'s own flush covers this.
            }
        }
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

        var payload = ShortcutLayoutJson.Serialize(layout, includeSecurity: true);
        if (payload.Length > MaxConfigBytes)
        {
            throw new InvalidOperationException("Shortcut data is too large to save.");
        }

        bool acquired;
        try
        {
            acquired = _fileMutex.WaitOne(TimeSpan.FromSeconds(FileMutexTimeoutSeconds));
        }
        catch (AbandonedMutexException)
        {
            // A prior QuickShell process (this one or QuickShell.Run) crashed while holding
            // the mutex. .NET still grants ownership to this waiter; our write is temp-file +
            // rename, so whatever is on disk right now is a consistent prior state either way.
            acquired = true;
        }

        if (!acquired)
        {
            RepositoryDiagnostics.Report("ShortcutRepository.WriteLayoutAtomic", "mutex-timeout");
            throw new IOException("Could not acquire the shortcut store lock.");
        }

        try
        {
            _fileWriter.WriteAllBytesAtomic(ConfigPath, payload);
        }
        finally
        {
            _fileMutex.ReleaseMutex();
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
            ShortcutLaunchNormalization.NormalizeShortcut(shortcut);
            Normalize(shortcut);
            normalized.Add(ShortcutLayoutEntry.FromShortcut(
                shortcut,
                entry.Security ?? new WorkspaceSecurityMetadata()));
        }

        AssignMissingShortcutIds(ShortcutLayoutJson.ExtractShortcuts(normalized));
        return normalized;
    }

    private void SyncShortcutsFromLayout(List<ShortcutLayoutEntry> layout, bool bumpStructuralVersion = true)
    {
        _snapshotVersion++;
        if (bumpStructuralVersion)
        {
            _structuralVersion++;
        }

        _shortcuts = ShortcutLayoutJson.ExtractShortcuts(layout).Select(Clone).ToArray();
        RebuildShortcutIndexes();
    }

    private void RebuildShortcutIndexes()
    {
        _shortcutsByName.Clear();
        _shortcutsById.Clear();

        foreach (var shortcut in _shortcuts)
        {
            if (!string.IsNullOrWhiteSpace(shortcut.Name))
            {
                _shortcutsByName.TryAdd(shortcut.Name, shortcut);
            }

            if (!string.IsNullOrWhiteSpace(shortcut.Id))
            {
                _shortcutsById.TryAdd(shortcut.Id, shortcut);
            }
        }
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

    private static ShortcutLayoutEntry? FindEntryById(List<ShortcutLayoutEntry> layout, string id) =>
        layout.FirstOrDefault(entry =>
            entry.Kind == ShortcutLayoutEntryKind.Shortcut
            && entry.Shortcut is not null
            && entry.Shortcut.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static StoredWorkspace ToStoredWorkspace(ShortcutLayoutEntry entry)
    {
        var security = entry.Security ?? new WorkspaceSecurityMetadata();
        return new StoredWorkspace(
            WorkspaceClone.Clone(entry.Shortcut!),
            security with { },
            Math.Max(1, security.Revision));
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
            _ => ShortcutLayoutEntry.FromShortcut(
                Clone(entry.Shortcut!),
                entry.Security ?? new WorkspaceSecurityMetadata()),
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
        var existingNames = WithLock(() =>
        {
            EnsureLoaded();
            return _shortcutsByName.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        });

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
        var parts = new List<string>
        {
            $"Imported {imported} shortcut{(imported == 1 ? "" : "s")}. Imported workspaces are untrusted until reviewed and trusted.",
        };

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

    private static TerminalShortcut Normalize(TerminalShortcut shortcut)
    {
        ShortcutLaunchNormalization.NormalizeShortcut(shortcut);

        var terminal = (shortcut.Terminal ?? string.Empty).Trim().ToLowerInvariant();
        shortcut.Terminal = terminal switch
        {
            TerminalCatalog.SameAsPreviousLaunchTargetId => TerminalCatalog.SameAsPreviousLaunchTargetId,
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

    internal static TerminalShortcut[] CloneAll(IEnumerable<TerminalShortcut> shortcuts) =>
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
        left.LastUsedUtc == right.LastUsedUtc &&
        string.Equals(left.DevServerUrl, right.DevServerUrl, StringComparison.Ordinal) &&
        left.OpenDevServerOnLaunch == right.OpenDevServerOnLaunch &&
        string.Equals(left.RepoUrl, right.RepoUrl, StringComparison.Ordinal) &&
        left.OpenCompanionAppOnLaunch == right.OpenCompanionAppOnLaunch &&
        string.Equals(left.CompanionAppPath, right.CompanionAppPath, StringComparison.Ordinal) &&
        string.Equals(left.CompanionAppArguments, right.CompanionAppArguments, StringComparison.Ordinal) &&
        LaunchListsEqual(left.Launches, right.Launches) &&
        CompanionListsEqual(left.CompanionApps, right.CompanionApps);

    private static bool LaunchListsEqual(List<WorkspaceEntry>? left, List<WorkspaceEntry>? right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal)
                || !string.Equals(a.Label, b.Label, StringComparison.Ordinal)
                || !string.Equals(a.Terminal, b.Terminal, StringComparison.Ordinal)
                || !string.Equals(a.WtProfile, b.WtProfile, StringComparison.Ordinal)
                || !string.Equals(a.Command, b.Command, StringComparison.Ordinal)
                || a.RunAsAdmin != b.RunAsAdmin
                || a.IsEnabled != b.IsEnabled
                || a.Order != b.Order
                || !string.Equals(TaskTypeCatalog.Normalize(a.TaskType), TaskTypeCatalog.Normalize(b.TaskType), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CompanionListsEqual(List<CompanionAppEntry>? left, List<CompanionAppEntry>? right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal)
                || !string.Equals(a.Path, b.Path, StringComparison.Ordinal)
                || !string.Equals(a.Arguments, b.Arguments, StringComparison.Ordinal)
                || a.OpenOnLaunch != b.OpenOnLaunch
                || a.Order != b.Order)
            {
                return false;
            }
        }

        return true;
    }

    internal static TerminalShortcut Clone(TerminalShortcut shortcut) => new()
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
        Launches = (shortcut.Launches ?? []).Select(WorkspaceMapper.CloneEntry).ToList(),
        CompanionApps = (shortcut.CompanionApps ?? []).Select(CompanionAppNormalization.CloneEntry).ToList(),
        DevServerUrl = shortcut.DevServerUrl,
        OpenDevServerOnLaunch = shortcut.OpenDevServerOnLaunch,
        RepoUrl = shortcut.RepoUrl,
        OpenCompanionAppOnLaunch = shortcut.OpenCompanionAppOnLaunch,
        CompanionAppPath = shortcut.CompanionAppPath,
        CompanionAppArguments = shortcut.CompanionAppArguments,
    };

    private void RaiseWorkspacesChanged() => WorkspacesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Bounds a wait on <see cref="_sync"/> instead of blocking forever, and reports slow
    /// holds so a stuck lock surfaces as a diagnosable event rather than a felt host freeze.
    /// Takes a plain method name (not a lambda) so hot paths like <see cref="Search"/> can call
    /// it without allocating a closure.
    /// </summary>
    private void AcquireLockOrThrow(string caller)
    {
        if (_sync.Wait(LockTimeout))
        {
            return;
        }

        RepositoryDiagnostics.Report($"ShortcutRepository.{caller}", "lock-timeout", (long)LockTimeout.TotalMilliseconds);
        throw new TimeoutException("Timed out waiting for the shortcut store lock.");
    }

    private void ReleaseLockAndReportSlow(string caller, long startTimestamp)
    {
        _sync.Release();
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        if (elapsed > SlowOperationThreshold)
        {
            RepositoryDiagnostics.Report($"ShortcutRepository.{caller}", "slow-operation", (long)elapsed.TotalMilliseconds);
        }
    }

    private void WithLock(Action action) =>
        WithLock(() =>
        {
            action();
            return true;
        });

    private T WithLock<T>(Func<T> action)
    {
        AcquireLockOrThrow(nameof(WithLock));
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return action();
        }
        finally
        {
            ReleaseLockAndReportSlow(nameof(WithLock), startTimestamp);
        }
    }

    private async Task WithLockAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        // Bound independently of the caller's token: a CancellationToken.None (or long-lived
        // lifetime token) would otherwise still wait forever if a holder never releases.
        if (!await _sync.WaitAsync(LockTimeout, cancellationToken).ConfigureAwait(false))
        {
            RepositoryDiagnostics.Report($"ShortcutRepository.{nameof(WithLockAsync)}", "lock-timeout", (long)LockTimeout.TotalMilliseconds);
            throw new TimeoutException("Timed out waiting for the shortcut store lock.");
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            ReleaseLockAndReportSlow(nameof(WithLockAsync), startTimestamp);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_persistTimer is not null)
        {
            // Timer.Dispose(WaitHandle) blocks until any in-flight callback has
            // finished, so the timer can never fire (or still be running) against
            // _sync/_fileMutex after this returns. Plain Dispose() gives no such
            // guarantee and left a shutdown race where a callback in flight could
            // call _sync.Wait() on an already-disposed semaphore.
            using var timerStopped = new ManualResetEvent(false);
            _persistTimer.Dispose(timerStopped);
            timerStopped.WaitOne();
        }

        try
        {
            WithLock(FlushPendingPersistLocked);
        }
        catch
        {
            // Best effort flush during shutdown.
        }

        _sync.Dispose();
        _fileMutex.Dispose();
        GC.SuppressFinalize(this);
    }
}


