namespace QuickShell.Services;

/// <summary>
/// Helpers for DynamicListPage search. Host <c>oldSearch</c> can desync from the
/// extension (e.g. after <c>SetSearchNoUpdate</c>); always compare against the last
/// query the page actually applied.
/// </summary>
internal static class ListSearchQuery
{
    public static string Normalize(string? query) => query ?? string.Empty;

    /// <summary>
    /// Returns true when the incoming search text differs from the last applied query.
    /// </summary>
    public static bool HasChanged(string appliedQuery, string? incomingQuery) =>
        !string.Equals(Normalize(appliedQuery), Normalize(incomingQuery), StringComparison.Ordinal);
}
