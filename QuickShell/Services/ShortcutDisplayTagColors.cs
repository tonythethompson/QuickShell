using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Services;

namespace QuickShell;

internal static class ShortcutDisplayTagColors
{
    public static OptionalColor ForAttention(WorkspaceAttentionState attention) =>
        attention switch
        {
            WorkspaceAttentionState.Blocking => ColorHelpers.FromRgb(232, 17, 35),
            WorkspaceAttentionState.Warning => ColorHelpers.FromRgb(255, 185, 0),
            WorkspaceAttentionState.Branch => ColorHelpers.FromRgb(255, 140, 0),
            _ => ColorHelpers.FromRgb(255, 185, 0),
        };
}
