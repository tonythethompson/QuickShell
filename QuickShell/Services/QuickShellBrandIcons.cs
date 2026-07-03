using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

internal static class QuickShellBrandIcons
{
    private const string RelativeIconPath = "Assets\\StoreLogo.png";

    /// <summary>Quick Shell logo for CmdPal top-level commands and extension pages (from logo-micro.svg).</summary>
    public static IconInfo App { get; } = CreateAppIcon();

    private static IconInfo CreateAppIcon()
    {
        var absolutePath = Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.png");
        return File.Exists(absolutePath)
            ? new IconInfo(absolutePath)
            : IconHelpers.FromRelativePath(RelativeIconPath);
    }
}
