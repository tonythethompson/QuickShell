using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class DotNetProjectClassifier : IProjectClassifier
{
    public string Name => "dotnet";

    public int Priority => 90;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        var hasDotNet = layout.HasCsproj
            || Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly)
                .Any(path =>
                    path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase));
        if (!hasDotNet)
        {
            return;
        }

        builder.TryClassifyDotNet();
    }
}
