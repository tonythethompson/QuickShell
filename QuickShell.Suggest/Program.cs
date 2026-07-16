using System.Text.Json;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Services;

if (!SuggestCommandLineArgs.TryParse(args, out var directory, out var usedCommands, out var generation))
{
    Console.Error.WriteLine("Usage: QuickShell.Suggest suggest --dir <path> [--used <command>]... [--generation N]");
    return 1;
}

var pills = CommandSuggestionService.GetPills(directory, usedCommands, ProjectAnalysisAccessor.Instance);
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
