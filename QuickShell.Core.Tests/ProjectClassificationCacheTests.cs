using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ProjectClassificationCacheTests : IDisposable
{
    private readonly string _root;

    public ProjectClassificationCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-classify-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Classify_SecondCall_ReusesCachedClassification()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var first = ProjectClassificationCache.Classify(_root);
        var second = ProjectClassificationCache.Classify(_root);

        Assert.True(first.Has(ProjectStack.Docker));
        Assert.Equal(first.Stacks, second.Stacks);
    }

    [Fact]
    public void Invalidate_ForcesReclassification()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        ProjectClassificationCache.Classify(_root);
        ProjectClassificationCache.Invalidate(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"), """{ "scripts": { "dev": "vite" } }""");

        var classification = ProjectClassificationCache.Classify(_root);

        Assert.True(classification.Has(ProjectStack.Node));
    }

    public void Dispose()
    {
        ProjectClassificationCache.Invalidate(_root);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
