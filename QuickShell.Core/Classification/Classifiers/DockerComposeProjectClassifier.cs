using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification.Classifiers;

internal sealed class DockerComposeProjectClassifier : IProjectClassifier
{
    public string Name => "docker-compose";

    public int Priority => 80;

    public void Contribute(string rootPath, ProjectLayout layout, ProjectClassificationBuilder builder)
    {
        if (!layout.HasDockerCompose)
        {
            return;
        }

        builder.TryClassifyDocker();
    }
}
