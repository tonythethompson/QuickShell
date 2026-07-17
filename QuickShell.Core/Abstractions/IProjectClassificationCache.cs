using QuickShell.Services;

namespace QuickShell.Abstractions;

/// <summary>
/// Provider-owned cache of <see cref="ProjectClassification"/> results keyed by directory.
/// Each DI container owns its own instance — never process-wide static state.
/// </summary>
internal interface IProjectClassificationCache
{
    ProjectClassification Classify(string? directory);

    void Invalidate(string? directory = null);
}
