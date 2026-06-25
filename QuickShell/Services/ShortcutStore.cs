using QuickShell.Models;
using System.Text.Json;
using System.Threading;

namespace QuickShell.Services;

internal static class ShortcutStore
{
    private const int MaxConfigBytes = 2 * 1024 * 1024;
    private const int MaxHistoryEntries = 50;

    private static readonly object Sync = new();
    private static readonly Mutex FileMutex = new(false, @"Global\QuickShell_shortcuts_json");

    private static TerminalShortcut[] _shortcuts = [];
    private static TerminalShortcut[] _lastGoodShortcuts = [];
    private static readonly List<TerminalShortcut[]> UndoHistory = [];
    private static readonly List<TerminalShortcut[]> RedoHistory = [];
    private static DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private static bool _configEnsured;
    private static bool _persistPending;
    private static Timer? _persistTimer;

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickShell");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "shortcuts.json");

    public static IReadOnlyList<TerminalShortcut> GetShortcuts()
    {
        lock (Sync)
        {
            EnsureLoaded();
            return CloneAll(_shortcuts);
        }
    }

    public static TerminalShortcut? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (Sync)
        {
            EnsureLoaded();
            var shortcut = _shortcuts.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return shortcut is null ? null : Clone(shortcut);
        }
    }

    public static void Reload()
    {
        lock (Sync)
        {
            CancelPendingPersist();
            _lastWriteTimeUtc = DateTime.MinValue;
            EnsureLoaded(force: true);
        }
    }

    public static void FlushPendingWrites()
    {
        lock (Sync)
        {
            FlushPendingPersistLocked();
        }
    }

    public static bool Undo()
    {
        lock (Sync)
        {
            EnsureLoaded();
            CancelPendingPersist();

            if (UndoHistory.Count == 0)
            {
                return false;
            }

            var current = CloneAll(_shortcuts);
            var previous = UndoHistory[^1];
            UndoHistory.RemoveAt(UndoHistory.Count - 1);
            PushHistory(RedoHistory, current);
            SaveLocked(previous);
            return true;
        }
    }

    public static bool Redo()
    {
        lock (Sync)
        {
            EnsureLoaded();
            CancelPendingPersist();

            if (RedoHistory.Count == 0)
            {
                return false;
            }

            var current = CloneAll(_shortcuts);
            var next = RedoHistory[^1];
            RedoHistory.RemoveAt(RedoHistory.Count - 1);
            PushHistory(UndoHistory, current);
            SaveLocked(next);
            return true;
        }
    }

    public static void Upsert(TerminalShortcut shortcut, string? originalName = null)
    {
        if (!ShortcutValidation.TryValidate(shortcut, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        if (!ShortcutValidation.TryValidateUniqueName(shortcut.Name, originalName, out validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        lock (Sync)
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
                shortcut.IsPinned = existing.IsPinned;
                shortcut.PinOrder = existing.PinOrder;
                shortcut.LastUsedUtc = existing.LastUsedUtc;
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
        }
    }

    public static bool Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (Sync)
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
        }
    }

    public static bool TogglePinned(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (Sync)
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
        }
    }

    public static bool MovePinned(string name, int direction)
    {
        if (string.IsNullOrWhiteSpace(name) || direction == 0)
        {
            return false;
        }

        lock (Sync)
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
        }
    }

    public static void MarkUsed(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (Sync)
        {
            EnsureLoaded();

            var list = _shortcuts.Select(Clone).ToList();
            var shortcut = list.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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
        }
    }

    public static TerminalShortcut? BuildDuplicate(string name)
    {
        var source = GetByName(name);
        if (source is null)
        {
            return null;
        }

        var copy = Clone(source);
        copy.Name = GetDuplicateName(copy.Name);
        copy.IsPinned = false;
        copy.PinOrder = null;
        copy.LastUsedUtc = null;
        return copy;
    }

    public static IEnumerable<TerminalShortcut> Search(string query)
    {
        var shortcuts = GetShortcuts();
        if (string.IsNullOrWhiteSpace(query))
        {
            return shortcuts;
        }

        return shortcuts.Where(shortcut => Matches(shortcut, query.Trim()));
    }

    private static void EnsureLoaded(bool force = false)
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
        }
        catch
        {
            _shortcuts = _lastGoodShortcuts.Length > 0 ? CloneAll(_lastGoodShortcuts) : [];
            _lastWriteTimeUtc = writeTime;
        }
    }

    private static void EnsureConfigExists()
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

    private static bool HasShortcutContent(string path)
    {
        return TryLoadShortcutsFromFile(path, out var shortcuts) && shortcuts.Length > 0;
    }

    private static bool TryImportShortcutsFromAlternateSources()
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

    private static IEnumerable<string> GetImportCandidatePaths()
    {
        yield return ConfigPath + ".bak";

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TerminalShortcutsCmdPal",
            "shortcuts.json");
    }

    private static bool TryLoadShortcutsFromFile(string path, out TerminalShortcut[] shortcuts)
    {
        shortcuts = [];

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxConfigBytes)
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize(json, QuickShellJsonContext.Default.ListTerminalShortcut);
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

    private static void SaveLocked(IReadOnlyCollection<TerminalShortcut> shortcuts)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var ordered = OrderForDisplay(shortcuts.Where(IsValidShortcut).Select(Normalize).Select(Clone));
        WriteShortcutsAtomic(ordered);
        _shortcuts = ordered;
        _lastGoodShortcuts = CloneAll(ordered);
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
    }

    private static void SchedulePersistLocked()
    {
        _persistPending = true;
        _persistTimer ??= new Timer(_ => FlushPendingPersistLocked(), null, Timeout.Infinite, Timeout.Infinite);
        _persistTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private static void CancelPendingPersist()
    {
        _persistPending = false;
        _persistTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void FlushPendingPersistLocked()
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

    private static void WriteShortcutsAtomic(TerminalShortcut[] shortcuts)
    {
        if (shortcuts.Length > ShortcutValidation.MaxShortcutCount)
        {
            throw new InvalidOperationException($"At most {ShortcutValidation.MaxShortcutCount} shortcuts are supported.");
        }

        var json = JsonSerializer.Serialize(shortcuts, QuickShellJsonContext.Default.TerminalShortcutArray);
        if (json.Length > MaxConfigBytes)
        {
            throw new InvalidOperationException("Shortcut data is too large to save.");
        }

        Directory.CreateDirectory(ConfigDirectory);

        var tempPath = ConfigPath + ".tmp";
        var backupPath = ConfigPath + ".bak";

        if (!FileMutex.WaitOne(TimeSpan.FromSeconds(5)))
        {
            throw new IOException("Could not acquire the shortcut store lock.");
        }

        try
        {
            File.WriteAllText(tempPath, json);

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
            FileMutex.ReleaseMutex();

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

    private static TerminalShortcut[] OrderForDisplay(IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts
            .OrderByDescending(s => s.IsPinned)
            .ThenBy(s => s.PinOrder ?? int.MaxValue)
            .ThenByDescending(s => s.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int NextPinOrder(IEnumerable<TerminalShortcut> list) =>
        list.Where(s => s.IsPinned).Select(s => s.PinOrder ?? 0).DefaultIfEmpty().Max() + 1;

    private static void SetPinOrder(List<TerminalShortcut> list, string name, int order)
    {
        var shortcut = list.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (shortcut is not null)
        {
            shortcut.PinOrder = order;
        }
    }

    private static string GetDuplicateName(string sourceName)
    {
        var existingNames = GetShortcuts().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static bool IsValidShortcut(TerminalShortcut shortcut) =>
        !string.IsNullOrWhiteSpace(shortcut.Name) && !string.IsNullOrWhiteSpace(shortcut.Directory);

    private static bool Matches(TerminalShortcut shortcut, string query)
    {
        if (shortcut.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
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

        if (shortcut.Directory.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.WtProfile) &&
            shortcut.WtProfile.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(shortcut.Command) &&
               shortcut.Command.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static TerminalShortcut Normalize(TerminalShortcut shortcut)
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

    private static TerminalShortcut[] CloneAll(IEnumerable<TerminalShortcut> shortcuts) =>
        shortcuts.Select(Clone).ToArray();

    private static void RecordHistoryLocked(
        IReadOnlyCollection<TerminalShortcut> previous,
        IReadOnlyCollection<TerminalShortcut> next)
    {
        var orderedNext = OrderForDisplay(next.Where(IsValidShortcut).Select(Normalize).Select(Clone));
        if (SnapshotEquals(previous, orderedNext))
        {
            return;
        }

        PushHistory(UndoHistory, previous);
        RedoHistory.Clear();
    }

    private static void PushHistory(List<TerminalShortcut[]> history, IEnumerable<TerminalShortcut> snapshot)
    {
        history.Add(CloneAll(snapshot));
        if (history.Count > MaxHistoryEntries)
        {
            history.RemoveAt(0);
        }
    }

    private static bool SnapshotEquals(IReadOnlyCollection<TerminalShortcut> left, TerminalShortcut[] right)
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

    private static bool ShortcutEquals(TerminalShortcut left, TerminalShortcut right) =>
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
}
