using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Composition;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class ShortcutFormTemplateJsonTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IProjectAnalysisService _projectAnalysis;
    private readonly ICommandSuggestionService _commandSuggestions;

    public ShortcutFormTemplateJsonTests()
    {
        _provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
        _projectAnalysis = _provider.GetRequiredService<IProjectAnalysisService>();
        _commandSuggestions = _provider.GetRequiredService<ICommandSuggestionService>();
    }
    public void Dispose() => _provider.Dispose();
}
