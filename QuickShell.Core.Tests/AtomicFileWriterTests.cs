using System.Text;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _directory;
    private readonly AtomicFileWriter _writer = new();

    public AtomicFileWriterTests()
    {
        _directory = Path.Join(Path.GetTempPath(), "quickshell-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void WriteAllBytesAtomic_first_create_uses_move_without_bak()
    {
        var path = Path.Join(_directory, "store.json");
        var payload = Encoding.UTF8.GetBytes("""{"ok":true}""");

        _writer.WriteAllBytesAtomic(path, payload);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(payload, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllBytesAtomic_replace_creates_bak_and_cleans_tmp()
    {
        var path = Path.Join(_directory, "store.json");
        File.WriteAllText(path, "v1");

        _writer.WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes("v2"));

        Assert.Equal("v2", File.ReadAllText(path));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("v1", File.ReadAllText(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllTextAtomic_creates_parent_directory()
    {
        var nested = Path.Join(_directory, "nested", "leaf.json");

        _writer.WriteAllTextAtomic(nested, """{"n":1}""");

        Assert.True(File.Exists(nested));
        Assert.Equal("""{"n":1}""", File.ReadAllText(nested));
    }

    [Fact]
    public void WriteAllBytesAtomic_cleans_preexisting_tmp_after_success()
    {
        var path = Path.Join(_directory, "store.json");
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, "stale");

        _writer.WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes("fresh"));

        Assert.Equal("fresh", File.ReadAllText(path));
        Assert.False(File.Exists(tmpPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
