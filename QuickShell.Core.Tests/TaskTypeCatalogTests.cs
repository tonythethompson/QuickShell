using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;
using System.Linq;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class TaskTypeCatalogTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;

    public TaskTypeCatalogTests()
    {
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
    }

    public void Dispose() => _provider.Dispose();
    [Fact]
    public void BuildPickerChoicesJson_WithoutDirectory_IncludesOnlyPlaceholder()
    {
        using var document = JsonDocument.Parse(TaskTypeCatalog.BuildPickerChoicesJson(_projectAnalysis));
        var values = document.RootElement
            .EnumerateArray()
            .Select(choice => choice.GetProperty("value").GetString())
            .ToList();

        Assert.Single(values);
        Assert.Equal(TaskTypeCatalog.None, values[0]);
    }

    [Fact]
    public void BuildPickerChoicesJson_ForDockerProject_IncludesLogsAndDatabaseOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "quickshell-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "docker-compose.yml"), "services: {}");

        try
        {
            using var document = JsonDocument.Parse(TaskTypeCatalog.BuildPickerChoicesJson(_projectAnalysis, root));
            var values = document.RootElement
                .EnumerateArray()
                .Select(choice => choice.GetProperty("value").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains(TaskTypeCatalog.None, values);
            Assert.Contains(TaskTypeCatalog.Logs, values);
            Assert.Contains(TaskTypeCatalog.Services, values);
            Assert.DoesNotContain(TaskTypeCatalog.Api, values);
            Assert.DoesNotContain(TaskTypeCatalog.Frontend, values);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void BuildPickerChoicesJson_IncludesTooltipOnEachChoice()
    {
        using var document = JsonDocument.Parse(TaskTypeCatalog.BuildPickerChoicesJson(_projectAnalysis));

        foreach (var choice in document.RootElement.EnumerateArray())
        {
            Assert.True(choice.TryGetProperty("tooltip", out _));
        }
    }

    [Fact]
    public void GetChoices_IncludesAgent()
    {
        Assert.Contains(TaskTypeCatalog.GetChoices(), choice => choice.Id == TaskTypeCatalog.Agent && choice.Title == "Agent");
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
    [InlineData("DATABASE", TaskTypeCatalog.Services)]
    [InlineData("database", TaskTypeCatalog.Services)]
    [InlineData("logs", TaskTypeCatalog.Logs)]
    [InlineData("agent", TaskTypeCatalog.Agent)]
    [InlineData("AI", TaskTypeCatalog.Agent)]
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
    [InlineData(TaskTypeCatalog.Services, "Services")]
    [InlineData(TaskTypeCatalog.Test, "Test")]
    [InlineData(TaskTypeCatalog.Build, "Build")]
    [InlineData(TaskTypeCatalog.Logs, "Logs")]
    [InlineData(TaskTypeCatalog.Agent, "Agent")]
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
    [InlineData(TaskTypeCatalog.Services)]
    [InlineData(TaskTypeCatalog.Test)]
    [InlineData(TaskTypeCatalog.Build)]
    [InlineData(TaskTypeCatalog.Logs)]
    [InlineData(TaskTypeCatalog.Agent)]
    public void GetGlyph_KnownValues_ReturnNonEmptyGlyphs(string value)
    {
        Assert.False(string.IsNullOrEmpty(TaskTypeCatalog.GetGlyph(value)));
    }

    [Theory]
    [InlineData(TaskTypeCatalog.Api, "🔵")]
    [InlineData(TaskTypeCatalog.Frontend, "🟢")]
    [InlineData(TaskTypeCatalog.Services, "⚪")]
    [InlineData(TaskTypeCatalog.Logs, "🟤")]
    [InlineData(TaskTypeCatalog.Test, "🟡")]
    [InlineData(TaskTypeCatalog.Build, "🟠")]
    [InlineData(TaskTypeCatalog.Agent, "🟣")]
    public void GetMarkerEmoji_KnownValues_ReturnExpectedMarkers(string value, string expected)
    {
        Assert.Equal(expected, TaskTypeCatalog.GetMarkerEmoji(value));
    }

    [Fact]
    public void GetMarkerEmoji_None_ReturnsNull()
    {
        Assert.Null(TaskTypeCatalog.GetMarkerEmoji(TaskTypeCatalog.None));
        Assert.Null(TaskTypeCatalog.GetMarkerEmoji(null));
    }

    [Theory]
    [InlineData(TaskTypeCatalog.Agent, "default")]
    [InlineData(TaskTypeCatalog.Test, "default")]
    [InlineData(TaskTypeCatalog.Api, "default")]
    [InlineData(TaskTypeCatalog.Build, "default")]
    [InlineData(TaskTypeCatalog.None, "default")]
    public void GetAdaptiveCardActionStyle_AlwaysDefault(string value, string expected)
    {
        Assert.Equal(expected, TaskTypeCatalog.GetAdaptiveCardActionStyle(value));
    }

    [Theory]
    [InlineData(TaskTypeCatalog.Api, 0xFF3498DBu)]
    [InlineData(TaskTypeCatalog.Frontend, 0xFF1ABC9Cu)]
    [InlineData(TaskTypeCatalog.Agent, 0xFF9B59B6u)]
    [InlineData(TaskTypeCatalog.Test, 0xFF27AE60u)]
    [InlineData(TaskTypeCatalog.Build, 0xFFE67E22u)]
    public void GetAccentArgb_KnownValues_ReturnStableColors(string value, uint expected)
    {
        Assert.Equal(expected, TaskTypeCatalog.GetAccentArgb(value));
    }

    [Fact]
    public void GetAccentArgb_Unknown_ReturnsGray()
    {
        Assert.Equal(0xFF808080u, TaskTypeCatalog.GetAccentArgb(null));
        Assert.Equal(0xFF808080u, TaskTypeCatalog.GetAccentArgb(TaskTypeCatalog.None));
    }
}
