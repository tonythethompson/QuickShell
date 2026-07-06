using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuickShell.Run;

internal static class RunWpfUiHelpers
{
    public static void SetTooltip(FrameworkElement element, string tooltip) =>
        element.ToolTip = tooltip;

    public static TextBlock FieldLabel(string text, string? tooltip = null)
    {
        var label = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 8, 0, 4),
        };
        if (!string.IsNullOrWhiteSpace(tooltip))
        {
            label.ToolTip = tooltip;
        }

        return label;
    }

    public static void EnableTabKeyboardNavigation(TabControl tabs)
    {
        KeyboardNavigation.SetTabNavigation(tabs, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(tabs, KeyboardNavigationMode.Cycle);
    }

    public static TabItem CreateTab(string header, UIElement content) =>
        new()
        {
            Header = header,
            Content = content,
        };
}
