using System.Text.Json;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CompanionAppFormJsonTests
{
    [Fact]
    public void BuildDataFields_EscapesJsonControlCharacters()
    {
        var fields = CompanionAppFormJson.BuildDataFields(
            [new CompanionAppFormRow
            {
                Preset = CompanionAppCatalog.PresetCustom,
                Path = "C:\\Apps\\Line\nBreak.exe",
                Arguments = "--label\tvalue\rnext",
            }],
            directory: "C:\\Workspace");

        using var document = JsonDocument.Parse("{" + string.Join(",", fields) + "}");
        Assert.Equal("C:\\Apps\\Line\nBreak.exe", document.RootElement.GetProperty("CompanionAppPresetTooltip_0").GetString());
        Assert.Equal("--label\tvalue\rnext", document.RootElement.GetProperty("CompanionAppArguments_0").GetString());
    }

    [Fact]
    public void BuildDataFields_UsesDefaultPresetTooltipWhenPathEmpty()
    {
        var fields = CompanionAppFormJson.BuildDataFields(
            [CompanionAppFormRow.Empty()],
            directory: "C:\\Workspace");

        using var document = JsonDocument.Parse("{" + string.Join(",", fields) + "}");
        Assert.Equal(
            WorkspaceFormTooltips.CompanionAppPreset,
            document.RootElement.GetProperty("CompanionAppPresetTooltip_0").GetString());
        Assert.False(document.RootElement.TryGetProperty("ShowCompanionExecutablePath_0", out _));
        Assert.False(document.RootElement.TryGetProperty("CompanionAppPathDisplay_0", out _));
    }

    [Fact]
    public void BuildSection_PlacesArgumentsBesideDropdownAndOmitsExecutableBlock()
    {
        var section = CompanionAppFormJson.BuildSection(
            [new CompanionAppFormRow
            {
                Preset = CompanionAppCatalog.PresetVs2026,
                Path = @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe",
                Arguments = "{solution}",
            }],
            companionChoicesJson: """[{"title":"Visual Studio 2026","value":"vs2026"}]""");

        Assert.Contains("CompanionAppArguments_0", section, StringComparison.Ordinal);
        Assert.Contains("\"$when\": \"${ShowCompanionArguments_0}\"", section, StringComparison.Ordinal);
        Assert.Contains("\"tooltip\": \"${CompanionAppPresetTooltip_0}\"", section, StringComparison.Ordinal);
        Assert.Contains(CompanionAppArgumentValidation.FieldLabel, section, StringComparison.Ordinal);
        Assert.Contains("\"width\": \"1\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Executable\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowCompanionExecutablePath", section, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanionAppPathDisplay", section, StringComparison.Ordinal);
    }
}