using System.Linq;

namespace QuickShell.Core.Tests.Architecture;

/// <summary>
/// Fails the build if production code schedules a fire-and-forget <c>Task.Run(...)</c>
/// without passing a cancellation token. Phase 1 replaced every untokened background
/// scheduler (row enrichment, directory-repair probes, suggestion scans, settings
/// refresh, git discovery) with lifetime-aware scheduling; this guard keeps new
/// fire-and-forget sites from reintroducing the gap.
/// </summary>
public sealed class TaskRunCancellationGuardTests
{
    private static readonly string[] ProductionRoots =
    [
        "QuickShell.Core",
        "QuickShell",
        "QuickShell.Run",
        "QuickShell.Suggest",
    ];

    private const string CallPrefix = "Task.Run(";

    [Fact]
    public void Production_code_never_calls_TaskRun_without_a_cancellation_token()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var directory in ProductionRoots.Select(root => Path.Join(repoRoot, root)))
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Expected production root was not found: {directory}");
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExcludedPath(file))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                var relative = Path.GetRelativePath(repoRoot, file);

                foreach (var _ in EnumerateTaskRunArgumentLists(text).Where(args => !ContainsTokenLikeIdentifier(args)))
                {
                    violations.Add($"{relative}: Task.Run(...) call has no CancellationToken argument");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Task.Run(...) calls missing a CancellationToken argument:" + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Finds every <c>Task.Run(</c> call site in <paramref name="text"/> and yields the
    /// full, balanced-paren argument-list text (including any lambda body) for each one.
    /// Lambda bodies routinely contain nested parens/braces, so this scans depth rather
    /// than using a single-line regex.
    /// </summary>
    private static IEnumerable<string> EnumerateTaskRunArgumentLists(string text)
    {
        var searchStart = 0;
        while (true)
        {
            var callStart = text.IndexOf(CallPrefix, searchStart, StringComparison.Ordinal);
            if (callStart < 0)
            {
                yield break;
            }

            // Ensure Task.Run is not part of a qualified name (e.g., SomeTask.Run)
            if (callStart > 0 && IsIdentifierChar(text[callStart - 1]))
            {
                searchStart = callStart + CallPrefix.Length;
                continue;
            }

            var argsStart = callStart + CallPrefix.Length;
            var depth = 1;
            var index = argsStart;
            var inString = false;
            var inChar = false;
            var inVerbatimString = false;
            while (index < text.Length && depth > 0)
            {
                var ch = text[index];

                // Track verbatim string literals (@"...")
                if (!inString && !inChar && ch == '@' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    inVerbatimString = true;
                    index += 2;
                    continue;
                }

                // Exit verbatim string on closing quote (doubled quotes are escaped)
                if (inVerbatimString)
                {
                    if (ch == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            index += 2; // Skip escaped quote
                            continue;
                        }
                        inVerbatimString = false;
                    }
                    index++;
                    continue;
                }

                // Track regular string literals
                if (!inChar && ch == '"' && !HasOddTrailingBackslashes(text, index))
                {
                    inString = !inString;
                    index++;
                    continue;
                }

                // Track char literals
                if (!inString && ch == '\'' && !HasOddTrailingBackslashes(text, index))
                {
                    inChar = !inChar;
                    index++;
                    continue;
                }

                // Only count parens outside of string/char literals
                if (!inString && !inChar)
                {
                    switch (ch)
                    {
                        case '(':
                            depth++;
                            break;
                        case ')':
                            depth--;
                            break;
                    }
                }

                index++;
            }

            // Unbalanced (shouldn't happen in compiling source) — stop scanning this file.
            if (depth != 0)
            {
                yield break;
            }

            yield return text[argsStart..(index - 1)];

            // Resume right after this call's own prefix (not past its whole argument
            // list) so a nested Task.Run(...) inside this call's arguments is still
            // found and checked on a later iteration.
            searchStart = callStart + CallPrefix.Length;
        }
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Returns true if the character at <paramref name="index"/> is preceded by an odd number
    /// of consecutive backslashes, indicating the character is escaped. Handles chains like
    /// \\" (even count, quote closes literal) vs \\\" (odd count, quote is escaped).
    /// </summary>
    private static bool HasOddTrailingBackslashes(string text, int index)
    {
        var backslashCount = 0;
        var i = index - 1;
        while (i >= 0 && text[i] == '\\')
        {
            backslashCount++;
            i--;
        }
        return (backslashCount % 2) == 1;
    }

    private static bool ContainsTokenLikeIdentifier(string argumentListText)
    {
        const string Token = "token";
        var span = argumentListText.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            var pos = span[index..].IndexOf(Token, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                return false;
            }

            var absolutePos = index + pos;
            var endPos = absolutePos + Token.Length;

            if (IsTokenWordBoundaryBefore(span, absolutePos) && IsTokenWordBoundaryAfter(span, endPos))
            {
                return true;
            }

            index = absolutePos + 1;
        }

        return false;
    }

    /// <summary>
    /// True when the character immediately before a "token" match starts a new identifier
    /// word: either it isn't an identifier character at all (e.g. "(", ",", "."), or it's a
    /// lowercase letter directly followed by the uppercase "T" of "Token" (a camelCase
    /// boundary, as in <c>cancellationToken</c> or <c>_cancellationToken</c>).
    /// </summary>
    private static bool IsTokenWordBoundaryBefore(ReadOnlySpan<char> span, int position)
    {
        if (position == 0 || !IsIdentifierChar(span[position - 1]))
        {
            return true;
        }

        return char.IsLower(span[position - 1]) && char.IsUpper(span[position]);
    }

    /// <summary>
    /// True when the character immediately after a "token" match ends the current identifier
    /// word: either it isn't an identifier character at all, or it's an uppercase letter that
    /// starts a new camelCase word (as in <c>tokenForTask</c>).
    /// </summary>
    private static bool IsTokenWordBoundaryAfter(ReadOnlySpan<char> span, int position)
    {
        if (position >= span.Length || !IsIdentifierChar(span[position]))
        {
            return true;
        }

        return char.IsUpper(span[position]);
    }

    private static bool IsExcludedPath(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".Tests/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "QuickShell.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate QuickShell.sln from test base directory.");
    }
}
