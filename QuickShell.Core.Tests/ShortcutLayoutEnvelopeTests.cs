using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutLayoutEnvelopeTests
{
    [Fact]
    public void TryParseRoot_AcceptsLegacyRootArray()
    {
        using var stream = new MemoryStream("""[{"Name":"Alpha","Directory":"C:\\\\work"}]"""u8.ToArray());

        Assert.True(ShortcutLayoutJson.TryParse(stream, out var layout));
        Assert.Single(layout);
        Assert.Equal("Alpha", layout[0].Shortcut?.Name);
    }

    [Fact]
    public void TryParseRoot_AcceptsVersionedEnvelope()
    {
        using var stream = new MemoryStream(
            """
            {
              "version": 1,
              "entries": [
                { "Name": "Alpha", "Directory": "C:\\\\work" }
              ]
            }
            """u8.ToArray());

        Assert.True(ShortcutLayoutJson.TryParse(stream, out var layout));
        Assert.Single(layout);
        Assert.Equal("Alpha", layout[0].Shortcut?.Name);
    }

    [Fact]
    public void Serialize_WritesVersionedEnvelope()
    {
        var payload = ShortcutLayoutJson.Serialize(
        [
            ShortcutLayoutEntry.FromShortcut(new TerminalShortcut
            {
                Name = "Alpha",
                Directory = @"C:\work",
            }),
        ]);

        var text = System.Text.Encoding.UTF8.GetString(payload);
        Assert.Contains("\"version\": 1", text);
        Assert.Contains("\"entries\"", text);
        Assert.Contains("\"Name\": \"Alpha\"", text);
    }

    [Fact]
    public async Task LegacyArray_OnSave_RewritesAsVersionedEnvelope()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);

        File.WriteAllText(
            Path.Combine(directory.Path, "shortcuts.json"),
            $$"""
            [
              { "Name": "Alpha", "Directory": "{{workspaceDirectory.Replace("\\", "\\\\")}}" }
            ]
            """);

        using (var repository = new ShortcutRepository(directory.Path))
        {
            await repository.PreloadAsync();
            repository.Upsert(new TerminalShortcut
            {
                Name = "Alpha",
                Directory = workspaceDirectory,
                Abbreviation = "a",
            }, originalName: "Alpha");
        }

        var saved = File.ReadAllText(Path.Combine(directory.Path, "shortcuts.json"));
        Assert.Contains("\"version\": 1", saved);
        Assert.Contains("\"entries\"", saved);
        Assert.DoesNotContain("\"version\": 1\n[", saved);
    }

    [Fact]
    public async Task VersionedEnvelope_RoundTripsThroughReload()
    {
        using var directory = new TempDataDirectory();
        var workspaceDirectory = Path.Combine(directory.Path, "Beta");
        Directory.CreateDirectory(workspaceDirectory);

        File.WriteAllBytes(
            Path.Combine(directory.Path, "shortcuts.json"),
            ShortcutLayoutJson.Serialize(
            [
                ShortcutLayoutEntry.FromShortcut(new TerminalShortcut
                {
                    Name = "Beta",
                    Directory = workspaceDirectory,
                    Abbreviation = "b",
                }),
            ]));

        using var repository = new ShortcutRepository(directory.Path);
        await repository.PreloadAsync();

        var shortcut = repository.GetByName("Beta");
        Assert.NotNull(shortcut);
        Assert.Equal("b", shortcut.Abbreviation);
    }

    [Fact]
    public void TryExportToFile_WritesVersionedEnvelope()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);
        var workspaceDirectory = Path.Combine(directory.Path, "Alpha");
        Directory.CreateDirectory(workspaceDirectory);
        repository.Upsert(new TerminalShortcut { Name = "Alpha", Directory = workspaceDirectory });

        var exportPath = Path.Combine(directory.Path, "export.json");
        Assert.True(repository.TryExportToFile(exportPath, out _));

        var exported = File.ReadAllText(exportPath);
        Assert.Contains("\"version\": 1", exported);
        Assert.Contains("\"entries\"", exported);
    }

    [Fact]
    public void ImportReplace_AcceptsVersionedEnvelopeFile()
    {
        using var directory = new TempDataDirectory();
        using var repository = new ShortcutRepository(directory.Path);

        var importPath = Path.Combine(directory.Path, "incoming.json");
        File.WriteAllBytes(
            importPath,
            ShortcutLayoutJson.Serialize(
            [
                ShortcutLayoutEntry.FromShortcut(new TerminalShortcut
                {
                    Name = "Imported",
                    Directory = @"C:\imported",
                }),
            ]));

        var result = repository.ImportReplace(importPath);

        Assert.True(result.Success);
        Assert.Single(repository.GetShortcuts());
        Assert.Equal("Imported", repository.GetShortcuts()[0].Name);
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public TempDataDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickshell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
