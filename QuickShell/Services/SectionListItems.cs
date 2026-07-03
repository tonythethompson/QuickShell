using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

/// <summary>
/// Emits CmdPal section headers per PowerToys PR #43952: a <see cref="Separator"/> row
/// (no command, non-empty section/title) renders the visible header; stamping
/// <see cref="IListItem.Section"/> on normal items does not.
/// </summary>
internal static class SectionListItems
{
    public static IEnumerable<IListItem> InSection(string sectionTitle, IEnumerable<IListItem> items)
    {
        var materialized = items.ToList();
        if (materialized.Count == 0)
        {
            return materialized;
        }

        if (string.IsNullOrWhiteSpace(sectionTitle))
        {
            return materialized;
        }

        return PrependHeader(sectionTitle, materialized);
    }

    public static IEnumerable<IListItem> PrependHeader(string sectionTitle, IReadOnlyList<IListItem> items)
    {
        yield return CreateHeader(sectionTitle);
        foreach (var item in items)
        {
            yield return item;
        }
    }

    public static Separator CreateHeader(string sectionTitle) => new(sectionTitle);
}
