using QuickShell.Abstractions.Classification;

namespace QuickShell.Classification;

internal static class ProjectClassificationContributor
{
    internal static void Try(
        IProjectClassifier classifier,
        string rootPath,
        ProjectLayout layout,
        ProjectClassificationBuilder builder)
    {
        try
        {
            classifier.Contribute(rootPath, layout, builder);
        }
        catch
        {
            // Repository discovery should degrade to fewer suggestions, not fail.
        }
    }
}
