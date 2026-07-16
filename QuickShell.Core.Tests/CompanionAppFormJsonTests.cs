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
        Assert.Equal("C:\\Apps\\Line\nBreak.exe", document.RootElement.GetProperty("CompanionAppPathDisplay_0").GetString());
        Assert.Equal("--label\tvalue\rnext", document.RootElement.GetProperty("CompanionAppArguments_0").GetString());
    }
}
