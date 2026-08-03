namespace QuickShell.Services;

internal sealed class TaskTypePickContext
{
    public static TaskTypePickContext Empty { get; } = new();

    public IReadOnlySet<string> UsedCommands { get; init; } = EmptyUsedCommands.Instance;

    public static TaskTypePickContext FromCommands(IEnumerable<string?> commands)
    {
        // Bolt: Performance optimization - avoid LINQ iterator allocations for parsing existing commands
        var usedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands.Where(static command => !string.IsNullOrWhiteSpace(command)))
        {
            usedCommands.Add(command!);
        }

        return new TaskTypePickContext
        {
            UsedCommands = usedCommands,
        };
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
