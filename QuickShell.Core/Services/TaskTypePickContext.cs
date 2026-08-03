using QuickShell.Models;

namespace QuickShell.Services;

internal sealed class TaskTypePickContext
{
    public static TaskTypePickContext Empty { get; } = new();

    public IReadOnlySet<string> UsedCommands { get; init; } = EmptyUsedCommands.Instance;

    public static TaskTypePickContext FromCommands(IEnumerable<string?> commands) =>
        new() { UsedCommands = CreateUsedCommandSet(commands) };

    /// <summary>
    /// Builds a case-insensitive set of non-blank commands for suggestion dedupe.
    /// Shared by <see cref="FromCommands"/> and agent/CLI suggestion providers.
    /// </summary>
    public static HashSet<string> CreateUsedCommandSet(IEnumerable<string?> commands)
    {
        var usedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                usedCommands.Add(command);
            }
        }

        return usedCommands;
    }

    /// <summary>
    /// Same as <see cref="CreateUsedCommandSet(IEnumerable{string?})"/> but walks launch
    /// entries directly to avoid an intermediate Select iterator allocation.
    /// </summary>
    public static HashSet<string> CreateUsedCommandSet(IReadOnlyList<WorkspaceEntry> launches)
    {
        var usedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var launch in launches)
        {
            var command = launch.Command;
            if (!string.IsNullOrWhiteSpace(command))
            {
                usedCommands.Add(command);
            }
        }

        return usedCommands;
    }

    private sealed class EmptyUsedCommands : IReadOnlySet<string>
    {
        public static EmptyUsedCommands Instance { get; } = new();

        public int Count => 0;

        public bool Contains(string item) => false;

        public IEnumerator<string> GetEnumerator()
        {
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsProperSubsetOf(IEnumerable<string> other) => false;

        public bool IsProperSupersetOf(IEnumerable<string> other) => false;

        public bool IsSubsetOf(IEnumerable<string> other) => true;

        public bool IsSupersetOf(IEnumerable<string> other) => false;

        public bool Overlaps(IEnumerable<string> other) => false;

        public bool SetEquals(IEnumerable<string> other) => !other.Any();
    }
}

internal sealed record TaskTypeCandidate(
    string Command,
    string Label,
    int Score,
    string Source);
