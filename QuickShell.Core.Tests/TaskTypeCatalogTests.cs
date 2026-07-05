using QuickShell.Services;
using System.Linq;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class TaskTypeCatalogTests
{
    [Fact]
    public void BuildFormChoicesJson_IncludesNoneAndAllFourTypes()
    {
        using var document = JsonDocument.Parse(TaskTypeCatalog.BuildFormChoicesJson());
        var values = document.RootElement
            .EnumerateArray()
            .Select(choice => choice.GetProperty("value").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(TaskTypeCatalog.None, values);
        Assert.Contains(TaskTypeCatalog.Api, values);
        Assert.Contains(TaskTypeCatalog.Frontend, values);
        Assert.Contains(TaskTypeCatalog.Database, values);
        Assert.Contains(TaskTypeCatalog.Logs, values);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void Normalize_UnknownOrBlank_ReturnsNone(string? value)
    {
        Assert.Equal(TaskTypeCatalog.None, TaskTypeCatalog.Normalize(value));
    }

    [Theory]
    [InlineData(" API ", TaskTypeCatalog.Api)]
    [InlineData("Frontend", TaskTypeCatalog.Frontend)]
    [InlineData("DATABASE", TaskTypeCatalog.Database)]
    [InlineData("logs", TaskTypeCatalog.Logs)]
    public void Normalize_KnownValues_AreCaseInsensitiveAndTrimmed(string value, string expected)
    {
        Assert.Equal(expected, TaskTypeCatalog.Normalize(value));
    }

    [Fact]
    public void GetTitle_None_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TaskTypeCatalog.GetTitle(TaskTypeCatalog.None));
        Assert.Equal(string.Empty, TaskTypeCatalog.GetTitle(null));
    }

    [Theory]
    [InlineData(TaskTypeCatalog.Api, "API")]
    [InlineData(TaskTypeCatalog.Frontend, "Frontend")]
    [InlineData(TaskTypeCatalog.Database, "Database")]
    [InlineData(TaskTypeCatalog.Logs, "Logs")]
    public void GetTitle_KnownValues_ReturnExpectedStrings(string value, string expected)
    {
        Assert.Equal(expected, TaskTypeCatalog.GetTitle(value));
    }

    [Fact]
    public void GetGlyph_None_ReturnsNull()
    {
        Assert.Null(TaskTypeCatalog.GetGlyph(TaskTypeCatalog.None));
        Assert.Null(TaskTypeCatalog.GetGlyph(null));
    }

    [Theory]
    [InlineData(TaskTypeCatalog.Api)]
    [InlineData(TaskTypeCatalog.Frontend)]
    [InlineData(TaskTypeCatalog.Database)]
    [InlineData(TaskTypeCatalog.Logs)]
    public void GetGlyph_KnownValues_ReturnNonEmptyGlyphs(string value)
    {
        Assert.False(string.IsNullOrEmpty(TaskTypeCatalog.GetGlyph(value)));
    }
}
