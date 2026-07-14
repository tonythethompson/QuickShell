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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Repository discovery should degrade to fewer suggestions, not fail.
        }
    }
}
