using QuickShell.Services;
using System.Text.Json.Nodes;

namespace QuickShell.Core.Tests;

public sealed class SettingsFormValueReaderTests
{
    [Fact]
    public void ReadString_ReturnsTrimmedJsonStringValue()
    {
        var values = JsonNode.Parse("""{"defaultProfile":"PowerShell 7"}""")!.AsObject();

        Assert.Equal("PowerShell 7", SettingsFormValueReader.ReadString(values, "defaultProfile"));
    }

    [Fact]
    public void ReadString_ReturnsNullWhenFieldMissing()
    {
        var values = JsonNode.Parse("""{"terminalApplication":"wt"}""")!.AsObject();

        Assert.Null(SettingsFormValueReader.ReadString(values, "defaultProfile"));
    }
}
