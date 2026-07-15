using System.Text.Json;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class SettingsCardJsonTests
{
    [Fact]
    public void SectionHeader_WithTooltip_EmitsValidJson()
    {
        var json = SettingsCardJson.SectionHeader("Backup & Transfer", "Medium", "Export and import workspaces.");

        using var document = JsonDocument.Parse(json);
        Assert.Equal("Export and import workspaces.", document.RootElement.GetProperty("tooltip").GetString());
    }
}
