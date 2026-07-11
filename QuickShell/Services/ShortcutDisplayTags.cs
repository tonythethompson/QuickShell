using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell;

internal static class ShortcutDisplayTags
{
    public static Tag[]? BuildTags(
        TerminalShortcut shortcut,
        string terminalApplicationId,
        string defaultProfileId)
    {
        if (!WorkspaceStatusService.TryGetCached(
                shortcut,
                terminalApplicationId,
                defaultProfileId,
                out var snapshot))
        {
            return null;
        }

        var tags = new List<Tag>(capacity: 2);

        if (snapshot.Attention != WorkspaceAttentionState.None)
        {
            tags.Add(new Tag(string.Empty)
            {
                Icon = new IconInfo(ShortcutGlyphs.IncidentTriangle),
                ToolTip = snapshot.AttentionSummary,
                Foreground = ShortcutDisplayTagColors.ForAttention(snapshot.Attention),
            });
        }

        if (snapshot.Activity == WorkspaceActivityState.Running)
        {
            tags.Add(new Tag(string.Empty)
            {
                Icon = new IconInfo(ShortcutGlyphs.Running),
                ToolTip = WorkspaceStatusLabels.RunningBadgeSummary,
            });
        }

        return tags.Count == 0 ? null : tags.ToArray();
    }
}
