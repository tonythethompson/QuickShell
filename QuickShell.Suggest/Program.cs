using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShell.Abstractions.Classification;
using QuickShell.Composition;
using QuickShell.Services;

if (!SuggestCommandLineArgs.TryParse(args, out var directory, out var usedCommands, out var generation))
{
    Console.Error.WriteLine("Usage: QuickShell.Suggest suggest --dir <path> [--used <command>]... [--generation N]");
    return 1;
}

using var provider = new ServiceCollection().AddQuickShellCore().BuildServiceProvider();
var projectAnalysis = provider.GetRequiredService<IProjectAnalysisService>();
var classificationCache = provider.GetRequiredService<QuickShell.Abstractions.IProjectClassificationCache>();

var pills = CommandSuggestionService.GetPills(directory, usedCommands, projectAnalysis, classificationCache);
var payload = new
{
    generation,
    pills = pills.Select(pill => new
    {
        command = pill.Command,
        taskType = pill.TaskType,
        typeTitle = pill.TypeTitle,
        displayTitle = pill.DisplayTitle,
        tooltip = pill.Tooltip,
    }),
};

Console.WriteLine(JsonSerializer.Serialize(payload));
return 0;
