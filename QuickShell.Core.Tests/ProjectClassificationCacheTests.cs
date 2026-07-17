using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ProjectClassificationCacheTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ProjectClassificationCache _cache;

    public ProjectClassificationCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "quickshell-classify-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _cache = new ProjectClassificationCache(_projectAnalysis);
    }

    [Fact]
    public void Classify_SecondCall_ReusesCachedClassification()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");

        var first = _cache.Classify(_root);
        var second = _cache.Classify(_root);

        Assert.True(first.Has(ProjectStack.Docker));
        Assert.Equal(first.Stacks, second.Stacks);
    }

    [Fact]
    public void Classify_RepeatedHotPathCalls_StayConsistent()
    {
        File.WriteAllText(Path.Combine(_root, "package.json"), """{ "scripts": { "dev": "vite" } }""");

        var first = _cache.Classify(_root);
        for (var i = 0; i < 20; i++)
        {
            var next = _cache.Classify(_root);
            Assert.Equal(first.Stacks, next.Stacks);
        }
    }

    [Fact]
    public void Invalidate_ForcesReclassification()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        _cache.Classify(_root);
        _cache.Invalidate(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"), """{ "scripts": { "dev": "vite" } }""");

        var classification = _cache.Classify(_root);

        Assert.True(classification.Has(ProjectStack.Node));
    }

    [Fact]
    public void BuildCheapSignature_StampsFsprojAfterTenCsprojFiles()
    {
        for (var i = 0; i < CommandSuggestionService.MaxRootProjects; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"{i:D2}.csproj"), "<Project />");
        }

        var project = Path.Combine(_root, "99.fsproj");
        File.WriteAllText(project, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var first = ProjectClassificationCache.BuildCheapSignature(_root);

        File.WriteAllText(project, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
        File.SetLastWriteTimeUtc(project, DateTime.UtcNow.AddSeconds(1));
        var second = ProjectClassificationCache.BuildCheapSignature(_root);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Classify_DetectsDotNetWhenSlnxAppearsWithoutInvalidation()
    {
        var before = _cache.Classify(_root);
        Assert.False(before.Has(ProjectStack.DotNet));

        File.WriteAllText(Path.Combine(_root, "QuickShell.slnx"), "<Solution />");

        var after = _cache.Classify(_root);

        Assert.True(after.Has(ProjectStack.DotNet));
    }

    [Fact]
    public void Classify_PicksUpVsCodeTasksJsonEditWithoutExplicitInvalidation()
    {
        var vscode = Path.Combine(_root, ".vscode");
        Directory.CreateDirectory(vscode);
        var tasksPath = Path.Combine(vscode, "tasks.json");
        File.WriteAllText(
            tasksPath,
            """
            {
              "version": "2.0.0",
              "tasks": [
                {
                  "label": "first",
                  "type": "shell",
                  "command": "echo",
                  "args": ["first"]
                }
              ]
            }
            """);

        var before = _cache.Classify(_root);
        Assert.True(before.Has(ProjectStack.VsCodeWorkspace));
        Assert.Contains(before.VsCodeTasks, task => task.Command.Contains("first", StringComparison.Ordinal));

        // In-place rewrite: parent directory mtime often stays put on Windows.
        File.WriteAllText(
            tasksPath,
            """
            {
              "version": "2.0.0",
              "tasks": [
                {
                  "label": "second",
                  "type": "shell",
                  "command": "echo",
                  "args": ["second-after-edit"]
                }
              ]
            }
            """);

        var after = _cache.Classify(_root);

        Assert.True(after.Has(ProjectStack.VsCodeWorkspace));
        Assert.Contains(after.VsCodeTasks, task => task.Command.Contains("second-after-edit", StringComparison.Ordinal));
        Assert.DoesNotContain(after.VsCodeTasks, task => task.Command.Contains("first", StringComparison.Ordinal));
    }

    [Fact]
    public void Classify_PicksUpNestedDevContainerJsonCreationWithoutInvalidation()
    {
        var before = _cache.Classify(_root);
        Assert.False(before.Has(ProjectStack.DevContainer));

        var devcontainer = Path.Combine(_root, ".devcontainer");
        Directory.CreateDirectory(devcontainer);
        File.WriteAllText(Path.Combine(devcontainer, "devcontainer.json"), "{}");

        var after = _cache.Classify(_root);
        Assert.True(after.Has(ProjectStack.DevContainer));
    }

    [Fact]
    public void BuildCheapSignature_ChangesWhenNestedDevContainerJsonIsEdited()
    {
        var devcontainer = Path.Combine(_root, ".devcontainer");
        Directory.CreateDirectory(devcontainer);
        var jsonPath = Path.Combine(devcontainer, "devcontainer.json");
        File.WriteAllText(jsonPath, """{ "name": "before" }""");

        var before = ProjectClassificationCache.BuildCheapSignature(_root);

        File.WriteAllText(jsonPath, """{ "name": "after-content-change-for-signature" }""");

        var after = ProjectClassificationCache.BuildCheapSignature(_root);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void BuildCheapSignature_ChangesWhenVsCodeTasksJsonIsEdited()
    {
        var vscode = Path.Combine(_root, ".vscode");
        Directory.CreateDirectory(vscode);
        var tasksPath = Path.Combine(vscode, "tasks.json");
        File.WriteAllText(tasksPath, """{ "version": "2.0.0", "tasks": [] }""");

        var before = ProjectClassificationCache.BuildCheapSignature(_root);

        File.WriteAllText(
            tasksPath,
            """{ "version": "2.0.0", "tasks": [ { "label": "x", "type": "shell", "command": "echo" } ] }""");

        var after = ProjectClassificationCache.BuildCheapSignature(_root);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void TwoCaches_DoNotShareState()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"), "services: {}");
        var other = new ProjectClassificationCache(_projectAnalysis);

        _ = _cache.Classify(_root);
        other.Invalidate();

        var viaOther = other.Classify(_root);
        Assert.True(viaOther.Has(ProjectStack.Docker));
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
